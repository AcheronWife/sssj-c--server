using System.Net;
using System.Net.Sockets;
using Gcg2OfflineServer.Models;
using Gcg2OfflineServer.Protocol;

namespace Gcg2OfflineServer.Services;

/// <summary>
/// TCP 游戏网关，监听 30400。
/// TCP 游戏网关：16字节包头 + Protobuf payload，实现 VERIFY→LOGIN 登录流程。
/// </summary>
public class TcpGateway
{
    private readonly TcpListener _listener;
    private readonly PlayerRepository _repo;
    private readonly GameLogger _logger;
    private readonly ServerListConfig _serverList;
    private readonly LuaDispatcher _lua;
    private readonly CancellationTokenSource _cts = new();
    private readonly HashSet<string> _activeAccounts = new();
    private readonly object _activeLock = new();

    public TcpGateway(string host, int port, PlayerRepository repo, GameLogger logger, ServerListConfig serverList)
    {
        _listener = new TcpListener(IPAddress.Parse(host), port);
        _repo = repo;
        _logger = logger;
        _serverList = serverList;
        _lua = new LuaDispatcher(repo, logger);
    }

    public async Task StartAsync()
    {
        _listener.Start();
        _logger.Info($"TCP gateway listening on {_listener.LocalEndpoint}");
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                _ = Task.Run(() => HandleClientAsync(client));
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Stop()
    {
        _cts.Cancel();
        _listener.Stop();
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        var endpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        _logger.Info($"gateway.connected peer={endpoint}");
        client.ReceiveTimeout = 120000;
        client.SendTimeout = 30000;
        var stream = client.GetStream();
        string? account = null;

        try
        {
            while (client.Connected && !_cts.IsCancellationRequested)
            {
                var packet = await ReadPacketAsync(stream);
                if (packet == null) break;

                switch (packet.Command)
                {
                    case Command.VerifyReq:
                        account = await HandleVerifyAsync(stream, packet);
                        break;

                    case Command.LoginReq:
                        account = await HandleLoginAsync(stream, packet, account);
                        break;

                    case Command.KeepAliveReq:
                        await SendAsync(stream, Command.KeepAliveRsp, packet.Serial);
                        break;

                    case Command.RenameReq:
                        if (account != null)
                        {
                            var name = MessageFactory.ParseRenameName(packet.Payload);
                            _repo.Rename(account, name);
                            _logger.Info($"player.rename account={account} name={name}");
                        }
                        await SendAsync(stream, Command.RenameRsp, packet.Serial);
                        break;

                    case Command.TaskValueReq:
                        if (account != null)
                        {
                            var p = _repo.Get(account);
                            if (p != null)
                                await SendAsync(stream, Command.TaskValueRsp, packet.Serial,
                                    MessageFactory.MakeTaskValueSync(p.TaskValues));
                        }
                        break;

                    case Command.TaskChangeReq:
                        if (account != null)
                        {
                            var changes = MessageFactory.ParseTaskChanges(packet.Payload);
                            _repo.SetTaskValues(account, changes);
                            _logger.Info($"task.change account={account} count={changes.Count}");
                            await SendAsync(stream, Command.TaskChangeRsp, packet.Serial, packet.Payload);
                        }
                        break;

                    case Command.GetHouseinfoReq:
                        var roleId = _repo.Get(account ?? "")?.RoleId ?? 1;
                        await SendAsync(stream, Command.GetHouseinfoRsp, packet.Serial,
                            MessageFactory.MakeHouseInfoResponse(roleId));
                        break;

                    case Command.HouseRandomReq:
                        await SendAsync(stream, Command.HouseRandomRsp, packet.Serial);
                        break;

                    case Command.C2sCallReq:
                        await HandleLuaCallAsync(stream, packet, account);
                        break;

                    default:
                        _logger.Warn($"gateway.unknown_cmd peer={endpoint} command={packet.Command}");
                        break;
                }
            }
        }
        catch (IOException ioEx) when (ioEx.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut })
        {
            _logger.Info($"gateway.idle_timeout peer={endpoint} (120s no data)");
        }
        catch (IOException ioEx) when (ioEx.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset })
        {
            // 客户端主动断开，正常情况，降级为 Info
            _logger.Info($"gateway.client_closed peer={endpoint}");
        }
        catch (Exception ex)
        {
            _logger.Error($"gateway.client_error peer={endpoint} {ex.Message}");
        }
        finally
        {
            if (account != null)
            {
                lock (_activeLock) { _activeAccounts.Remove(account); }
            }
            client.Close();
            _logger.Info($"gateway.disconnected peer={endpoint}");
        }
    }

    // ---- VERIFY (1102 → 1103) ----

    private async Task<string?> HandleVerifyAsync(NetworkStream stream, ParsedPacket packet)
    {
        try
        {
            var fields = ProtobufReader.Decode(packet.Payload);
            var platform = ProtobufReader.FirstString(fields, 1);
            var account = ProtobufReader.FirstString(fields, 2, "offline");

            // 拒绝空账号
            if (string.IsNullOrWhiteSpace(account))
            {
                _logger.Warn($"gateway.reject_empty_account peer={stream.Socket.RemoteEndPoint}");
                return null;
            }

            // 并发登录保护：同一账号同时只能一个连接，新连接踢旧连接
            lock (_activeLock)
            {
                if (_activeAccounts.Contains(account))
                {
                    _logger.Info($"gateway.duplicate_login account={account} 踢掉旧连接");
                }
                _activeAccounts.Add(account);
            }

            var player = _repo.GetOrCreate(account);
            var isNew = player.IsNewPlayer;

            _logger.Info($"session.verified account={account} platform={platform} roleId={player.RoleId} isNew={isNew}");

            await SendAsync(stream, Command.VerifyRsp, packet.Serial,
                MessageFactory.MakeVerifyResponse(player, _serverList, isNew));

            return account;
        }
        catch (Exception ex)
        {
            _logger.Error($"verify.error {ex.Message}");
            return null;
        }
    }

    // ---- LOGIN (1001 → 1026 → ... → 1002) ----

    private async Task<string?> HandleLoginAsync(NetworkStream stream, ParsedPacket packet, string? existingAccount)
    {
        try
        {
            var fields = ProtobufReader.Decode(packet.Payload);
            var account = ProtobufReader.FirstString(fields, 1, existingAccount ?? "offline");
            var clientUserState = ProtobufReader.FirstNumber(fields, 9);
            var channel = ProtobufReader.FirstString(fields, 10);

            var player = _repo.GetOrCreate(account);
            var isNew = player.IsNewPlayer;
            player = _repo.MarkLogin(account) ?? player;

            _logger.Info($"session.login account={account} roleId={player.RoleId} channel={channel} isNew={isNew} clientUserState={clientUserState}");

            await SendAsync(stream, Command.TaskValueRsp, 0, MessageFactory.MakeTaskValueSync(player.TaskValues));
            await Task.Delay(20);

            await SendAsync(stream, Command.Live2dEnableLevelNtf, 0, MessageFactory.MakeLive2dEnableLevel(player.Live2dEnableLevel));
            await SendAsync(stream, Command.Live2dHxStateNtf, 0, MessageFactory.MakeLive2dHxState(player.Live2dHx));
            await Task.Delay(20);

            var playerNtfData = MessageFactory.MakePlayerNotification(player);
            var ch10Count = player.Levels.Count(l => (int)(l.Id >> 16) == 10);
            _logger.Info($"login.player_ntf account={account} levels={player.Levels.Count} chapter10={ch10Count} bytes={playerNtfData.Length}");
            await SendAsync(stream, Command.PlayerNtf, 0, playerNtfData);
            await Task.Delay(20);

            await SendAsync(stream, Command.ItemNtf, 0, MessageFactory.MakeItemNotification(player));
            await Task.Delay(20);

            await SendAsync(stream, Command.PhoneMsgNtf, 0, MessageFactory.MakePhoneMessageNotification(player));
            await Task.Delay(40);

            await SendAsync(stream, Command.LoginRsp, packet.Serial);

            _logger.Info($"login.complete account={account}");
            return account;
        }
        catch (Exception ex)
        {
            _logger.Error($"login.error {ex.Message}");
            return null;
        }
    }

    // ---- Lua 调用 (1022 → 1023 + 1024) ----

    private async Task HandleLuaCallAsync(NetworkStream stream, ParsedPacket packet, string? account)
    {
        await SendAsync(stream, Command.C2sCallRsp, packet.Serial);

        if (string.IsNullOrEmpty(account))
        {
            _logger.Warn("lua.call without account, ignoring");
            return;
        }

        var call = LuaDispatcher.ParseCall(packet.Payload);
        if (call == null)
        {
            _logger.Warn($"lua.call.parse_failed account={account} payloadLen={packet.Payload.Length}");
            return;
        }

        var (method, parameters) = call.Value;
        _logger.Info($"lua.call account={account} method={method}");

        List<byte[]> responses;
        LuaCallResult result;
        try
        {
            (responses, result) = _lua.Handle(account, method, parameters);
        }
        catch (Exception ex)
        {
            _logger.Error($"lua.handle_error account={account} method={method} {ex.Message}");
            return;
        }

        try
        {
            if (result.Formation != null)
            {
                await SendAsync(stream, Command.FormationUpdateNtf, 0, MessageFactory.MakeFormationUpdateNotification(result.Formation));
                await Task.Delay(10);
            }

            var player = _repo.Get(account);
            if (result.UpdatedItems.Count > 0)
            {
                await SendAsync(stream, Command.ItemUpdateNtf, 0, MessageFactory.MakeItemUpdateNotification(result.UpdatedItems));
                await Task.Delay(5);
            }
            if (result.UpdatedGirls.Count > 0)
            {
                await SendAsync(stream, Command.GirlUpdateNtf, 0, MessageFactory.MakeGirlUpdateNotification(result.UpdatedGirls));
                await Task.Delay(5);
            }
            foreach (var money in result.UpdatedMoney)
            {
                await SendAsync(stream, Command.MoneyUpdateNtf, 0, MessageFactory.MakeMoneyUpdateNotification(money));
                await Task.Delay(5);
            }
            if (result.NeedsPlayerSync && player != null)
            {
                await SendAsync(stream, Command.TaskValueRsp, 0, MessageFactory.MakeTaskValueSync(player.TaskValues));
                await Task.Delay(5);
            }
            if (result.ExperienceChanged && player != null)
            {
                await SendAsync(stream, Command.PlayerUpdateNtf, 0, MessageFactory.MakePlayerUpdateNotification(player));
                await Task.Delay(5);
            }

            foreach (var respPayload in responses)
            {
                await SendAsync(stream, Command.NtfS2cCall, 0, respPayload);
                await Task.Delay(10);
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"lua.send_error account={account} method={method} {ex.Message}");
        }
    }

    // ---- 帧读写 ----

    private static async Task SendAsync(NetworkStream stream, ushort command, uint serial, byte[]? payload = null, ushort returnCode = 0)
    {
        var packet = GamePacket.Make(command, serial, payload, returnCode);
        await stream.WriteAsync(packet);
        await stream.FlushAsync();
    }

    /// <summary>
    /// 读取一个完整包。先读 16 字节头，根据 size 读 payload。处理粘包。
    /// </summary>
    private static async Task<ParsedPacket?> ReadPacketAsync(NetworkStream stream)
    {
        var header = new byte[GamePacket.HeaderSize];
        var read = await ReadExactAsync(stream, header);
        if (read < GamePacket.HeaderSize) return null;

        var size = BitConverter.ToUInt32(header, 4);
        if (size < GamePacket.HeaderSize || size > 16 * 1024 * 1024)
            throw new InvalidDataException($"Invalid packet size: {size}");

        var payloadLen = (int)(size - GamePacket.HeaderSize);
        var fullPacket = new byte[size];
        Buffer.BlockCopy(header, 0, fullPacket, 0, GamePacket.HeaderSize);

        if (payloadLen > 0)
        {
            var payloadRead = await ReadExactAsync(stream, fullPacket.AsMemory(GamePacket.HeaderSize, payloadLen));
            if (payloadRead < payloadLen) return null;
        }

        return GamePacket.Read(fullPacket);
    }

    private static async Task<int> ReadExactAsync(NetworkStream stream, Memory<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer[total..]);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
