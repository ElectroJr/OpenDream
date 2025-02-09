using System.Diagnostics.CodeAnalysis;
using OpenDreamRuntime.Objects;
using OpenDreamShared.Network.Messages;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;

namespace OpenDreamRuntime {
    sealed partial class DreamManager {
        [Dependency] private readonly IServerNetManager _netManager = default!;

        private readonly Dictionary<NetUserId, DreamConnection> _connections = new();

        public IEnumerable<DreamConnection> Connections => _connections.Values;

        private void InitializeConnectionManager() {
            _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;

            _netManager.RegisterNetMessage<MsgUpdateStatPanels>();
            _netManager.RegisterNetMessage<MsgUpdateAvailableVerbs>();
            _netManager.RegisterNetMessage<MsgSelectStatPanel>(RxSelectStatPanel);
            _netManager.RegisterNetMessage<MsgOutput>();
            _netManager.RegisterNetMessage<MsgAlert>();
            _netManager.RegisterNetMessage<MsgPrompt>();
            _netManager.RegisterNetMessage<MsgPromptList>();
            _netManager.RegisterNetMessage<MsgPromptResponse>(RxPromptResponse);
            _netManager.RegisterNetMessage<MsgBrowseResource>();
            _netManager.RegisterNetMessage<MsgBrowse>();
            _netManager.RegisterNetMessage<MsgTopic>(RxTopic);
            _netManager.RegisterNetMessage<MsgWinSet>();
            _netManager.RegisterNetMessage<MsgWinClone>();
            _netManager.RegisterNetMessage<MsgWinExists>();
            _netManager.RegisterNetMessage<MsgLoadInterface>();
            _netManager.RegisterNetMessage<MsgAckLoadInterface>(RxAckLoadInterface);
            _netManager.RegisterNetMessage<MsgSound>();
        }

        private void RxSelectStatPanel(MsgSelectStatPanel message) {
            var connection = ConnectionForChannel(message.MsgChannel);
            connection.HandleMsgSelectStatPanel(message);
        }

        private void RxPromptResponse(MsgPromptResponse message) {
            var connection = ConnectionForChannel(message.MsgChannel);
            connection.HandleMsgPromptResponse(message);
        }

        private void RxTopic(MsgTopic message) {
            var connection = ConnectionForChannel(message.MsgChannel);
            connection.HandleMsgTopic(message);
        }

        private void RxAckLoadInterface(MsgAckLoadInterface message) {
            // Once the client loaded the interface, move them to in-game.
            var player = _playerManager.GetSessionByChannel(message.MsgChannel);
            player.JoinGame();
        }

        private DreamConnection ConnectionForChannel(INetChannel channel) {
            return _connections[_playerManager.GetSessionByChannel(channel).UserId];
        }

        private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e) {
            switch (e.NewStatus) {
                case SessionStatus.Connected:
                    var interfaceResource = _dreamResourceManager.LoadResource(_compiledJson.Interface);
                    var msgLoadInterface = new MsgLoadInterface() {
                        InterfaceText = interfaceResource.ReadAsString()
                    };

                    e.Session.ConnectedClient.SendMessage(msgLoadInterface);
                    break;
                case SessionStatus.InGame: {
                    if (!_connections.TryGetValue(e.Session.UserId, out var connection)) {
                        connection = new DreamConnection();

                        _connections.Add(e.Session.UserId, connection);
                    }

                    connection.HandleConnection(e.Session);
                    break;
                }
                case SessionStatus.Disconnected: {
                    DreamConnection connection = GetConnectionBySession(e.Session);

                    connection.HandleDisconnection();
                    break;
                }
            }
        }

        private void UpdateStat() {
            foreach (var connection in _connections.Values) {
                connection.UpdateStat();
            }
        }

        public DreamConnection GetConnectionBySession(IPlayerSession session) {
            return _connections[session.UserId];
        }

        public DreamConnection GetConnectionFromClient(DreamObject client) {
            foreach (DreamConnection potentialConnection in Connections) {
                if (potentialConnection.Client == client)
                    return potentialConnection;
            }

            throw new Exception($"Client {client} does not belong to a connection");
        }

        public bool TryGetConnectionFromMob(DreamObject mob, [NotNullWhen(true)] out DreamConnection? connection) {
            foreach (DreamConnection potentialConnection in Connections) {
                if (potentialConnection.Mob == mob) {
                    connection = potentialConnection;
                    return true;
                }
            }

            connection = null;
            return false;
        }
    }
}
