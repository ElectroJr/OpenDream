﻿using JetBrains.Annotations;
using OpenDreamClient.Input.ContextMenu;
using OpenDreamClient.Interface.Controls;
using OpenDreamClient.Rendering;
using OpenDreamShared.Dream;
using OpenDreamShared.Input;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;

namespace OpenDreamClient.Input {
    sealed class MouseInputSystem : SharedMouseInputSystem {
        [Dependency] private readonly IInputManager _inputManager = default!;
        [Dependency] private readonly IUserInterfaceManager _userInterfaceManager = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IOverlayManager _overlayManager = default!;
        [Dependency] private readonly EntityLookupSystem _lookupSystem = default!;

        private DreamViewOverlay _dreamViewOverlay;
        private ContextMenuPopup _contextMenu;

        public override void Initialize() {
            _contextMenu = new ContextMenuPopup();
            _userInterfaceManager.ModalRoot.AddChild(_contextMenu);
        }

        public override void Shutdown() {
            CommandBinds.Unregister<MouseInputSystem>();
        }

        public bool HandleViewportClick(ScalingViewport viewport, GUIBoundKeyEventArgs args) {
            UIBox2i viewportBox = viewport.GetDrawBox();
            if (!viewportBox.Contains((int)args.RelativePixelPosition.X, (int)args.RelativePixelPosition.Y))
                return false; // Click was outside of the viewport

            bool shift = _inputManager.IsKeyDown(Keyboard.Key.Shift);
            bool ctrl = _inputManager.IsKeyDown(Keyboard.Key.Control);
            bool alt = _inputManager.IsKeyDown(Keyboard.Key.Alt);

            Vector2 screenLocPos = (args.RelativePixelPosition - viewportBox.TopLeft) / viewportBox.Size;
            screenLocPos *= viewport.ViewportSize;
            screenLocPos.Y = viewport.ViewportSize.Y - screenLocPos.Y; // Flip the Y
            ScreenLocation screenLoc = new ScreenLocation((int) screenLocPos.X, (int) screenLocPos.Y, 32); // TODO: icon_size other than 32

            MapCoordinates mapCoords = viewport.ScreenToMap(args.PointerLocation.Position);
            RendererMetaData? entity = GetEntityUnderMouse(screenLocPos);
            if (entity == null)
                return false;

            if (entity.ClickUID == EntityUid.Invalid && args.Function != EngineKeyFunctions.UIRightClick) { // Turf was clicked and not a right-click
                // Grid coordinates are half a meter off from entity coordinates
                mapCoords = new MapCoordinates(mapCoords.Position + 0.5f, mapCoords.MapId);

                if (_mapManager.TryFindGridAt(mapCoords, out var grid)){
                    Vector2i position = grid.CoordinatesToTile(mapCoords);
                    MapCoordinates worldPosition = grid.GridTileToWorld(position);
                    Vector2i turfIconPosition = (Vector2i) ((mapCoords.Position - position) * EyeManager.PixelsPerMeter);
                    RaiseNetworkEvent(new TurfClickedEvent(position, (int)worldPosition.MapId, screenLoc,  shift, ctrl, alt, turfIconPosition));
                }

                return true;
            }

            if (args.Function == EngineKeyFunctions.UIRightClick) { //either turf or atom was clicked, and it was a right-click
                var entities = _lookupSystem.GetEntitiesInRange(mapCoords, 0.01f);
                //TODO filter entities by the valid verbs that exist on them
                //they should only show up if there is a verb attached to usr which matches the filter in world syntax
                //ie, obj|turf in world
                //note that popup_menu = 0 overrides this behaviour, as does verb invisibility (urgh), and also hidden
                //because BYOND sure loves redundancy
                if(entities.Count == 0)
                    return true; //don't open a 1x1 empty context menu
                _contextMenu.RepopulateEntities(entities);
                _contextMenu.Measure(_userInterfaceManager.ModalRoot.Size);
                Vector2 contextMenuLocation = args.PointerLocation.Position / _userInterfaceManager.ModalRoot.UIScale; // Take scaling into account
                _contextMenu.Open(UIBox2.FromDimensions(contextMenuLocation, _contextMenu.DesiredSize));

                return true;
            }

            // TODO: Take icon transformations into account
            Vector2i iconPosition = (Vector2i) ((mapCoords.Position - entity.Position) * EyeManager.PixelsPerMeter);
            RaiseNetworkEvent(new EntityClickedEvent(entity.ClickUID, screenLoc, shift, ctrl, alt, iconPosition));
            return true;
        }

        [CanBeNull]
        private RendererMetaData GetEntityUnderMouse(Vector2 mousePos) {
            _dreamViewOverlay ??= _overlayManager.GetOverlay<DreamViewOverlay>();
            if(_dreamViewOverlay.MouseMap == null)
                return null;

            Color lookupColor = _dreamViewOverlay.MouseMap.GetPixel((int)mousePos.X, (int)mousePos.Y);
            if(!_dreamViewOverlay.MouseMapLookup.TryGetValue(lookupColor, out var result))
                return null;

            return result;
        }
    }
}
