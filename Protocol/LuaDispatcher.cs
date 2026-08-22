using System.Linq;
using System.Text.Json;
using Gcg2OfflineServer.GameData;
using Gcg2OfflineServer.Models;
using Gcg2OfflineServer.Services;
using static Gcg2OfflineServer.Protocol.ProtobufWriter;

namespace Gcg2OfflineServer.Protocol;

/// <summary>
/// 一次 Lua 调用的增量同步结果，供 TcpGateway 发送通知包。
/// 替代原实例字段，避免并发调用互相覆盖。
/// </summary>
public class LuaCallResult
{
    public bool NeedsPlayerSync { get; set; }
    public FormationState? Formation { get; set; }
    public List<MoneyEntry> UpdatedMoney { get; } = new();
    public List<InventoryEntry> UpdatedItems { get; } = new();
    public List<GirlState> UpdatedGirls { get; } = new();
    public bool ExperienceChanged { get; set; }
}

/// <summary>
/// Lua 调用分发器（处理 C2S_CALL_REQ 1022）。
/// 游戏内大部分交互（剧情、战斗、咖啡馆、抽卡、养成）都通过 Lua 调用走这里。
/// 核心命令已实现，未实现的命令返回合法空响应（不崩溃），可按需扩展。
/// 并发安全：所有玩家修改通过 PlayerRepository.Modify 在 account 锁内执行。
/// </summary>
public class LuaDispatcher
{
    private readonly PlayerRepository _repo;
    private readonly GameLogger _logger;

    public LuaDispatcher(PlayerRepository repo, GameLogger logger)
    {
        _repo = repo;
        _logger = logger;
    }

    /// <summary>
    /// 解析并处理一个 Lua 调用。
    /// 返回响应 payload 列表 + 增量同步结果（供 TcpGateway 发通知）。
    /// </summary>
    public (List<byte[]> responses, LuaCallResult result) Handle(string account, string method, JsonElement parameters)
    {
        var result = new LuaCallResult();
        var responses = new List<byte[]>();

        try
        {
            switch (method)
            {
                case "GirlLogic":
                    HandleGirlLogic(account, parameters, responses, result);
                    break;
                case "LuaCall":
                    HandleLuaCall(account, parameters, responses, result);
                    break;
                case "ChapterMsg":
                    HandleChapterMsg(account, parameters, responses, result);
                    break;
                case "SignUpMsg":
                    HandleSignUp(account, parameters, responses, result);
                    break;
                case "MissionGetAward":
                case "MissionActiveAward":
                    HandleMissionAward(account, method, parameters, responses, result);
                    break;
                case "GuideMissionGetAward":
                case "GuideProgressGetAward":
                    HandleGuideAward(account, method, parameters, responses, result);
                    break;
                case "NormalActivityGetAward":
                    HandleNormalActivityAward(account, parameters, responses, result);
                    break;
                case "LockItem":
                    HandleLockItem(account, parameters, responses, result);
                    break;
                case "PhoneMsg":
                    HandlePhoneMsg(account, parameters, responses, result);
                    break;
                case "Lottery":
                case "GetFirstGacha":
                    HandleLottery(account, method, parameters, responses, result);
                    break;
                case "WeaponLogicMsg":
                    HandleWeaponLogic(account, parameters, responses, result);
                    break;
                case "ShopGoodsList":
                    responses.Add(MakeS2CCall("ShopGoodsList", new { nError = 0, tbShop = Array.Empty<object>() }));
                    break;
                case "StartTrain":
                case "EndTrain":
                case "GirlGift":
                case "LevelAward":
                case "SetMainGirl":
                case "ChangeCloth":
                case "SetModelInFight":
                case "HeadTouched":
                    HandleGirlLogic(account, parameters, responses, result, method);
                    break;
                case "ExChangeMoney":
                    HandleExChangeMoney(account, parameters, responses, result);
                    break;
                case "NCafePetLogic":
                    HandleCafePet(parameters, responses, result, account);
                    break;
                case "GetLastGacha":
                    HandleGetLastGacha(account, responses, result);
                    break;
                case "SetCardCustomUp":
                case "SetWeaponCustomUp":
                    HandleSetCustomUp(account, method, parameters, responses, result);
                    break;
                case "BountyJoin":
                case "BountyPass":
                case "BountyFail":
                    responses.Add(MakeS2CCall(method, new { nError = 0 }));
                    break;
                default:
                    var paramJson = parameters.GetRawText();
                    _logger.Info($"lua.unknown_method method={method} params={paramJson}");
                    responses.Add(MakeS2CCall(method, new { nError = 0 }));
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"lua.handle_error method={method} {ex.Message}");
            responses.Add(MakeS2CCall(method, new { nError = 1 }));
        }

        return (responses, result);
    }

    // ---- 内联的简单 handler（从 switch 抽出，保持可读性）----

    private void HandleExChangeMoney(string account, JsonElement parameters, List<byte[]> responses, LuaCallResult result)
    {
        var exType = parameters.TryGetProperty("nType", out var et) ? et.GetInt64() : 0;
        var exCount = parameters.TryGetProperty("nCount", out var ec) ? ec.GetInt64() : 0;
        if (exType > 0 && exCount > 0)
        {
            _repo.Modify(account, player =>
            {
                var srcMoney = player.Money.FirstOrDefault(m => m.Id == exType);
                if (srcMoney != null && srcMoney.Count >= exCount)
                {
                    srcMoney.Count -= exCount;
                    long targetType = exType == 1 ? 2 : exType == 2 ? 3 : 1;
                    var tgtMoney = player.Money.FirstOrDefault(m => m.Id == targetType);
                    if (tgtMoney != null) tgtMoney.Count += exCount;
                    result.UpdatedMoney.Add(srcMoney);
                    if (tgtMoney != null) result.UpdatedMoney.Add(tgtMoney);
                }
            });
        }
        _logger.Info($"lua.exChangeMoney account={account} type={exType} count={exCount}");
        responses.Add(MakeS2CCall("ExChangeMoney", new { nError = 0 }));
    }

    private void HandleCafePet(JsonElement parameters, List<byte[]> responses, LuaCallResult result, string account)
    {
        var petCmd = parameters.TryGetProperty("sCmd", out var pc) ? pc.GetString() : "";
        if (petCmd == "GetCafeFoodNum")
            responses.Add(MakeS2CCall("NCafePetLogic", new { sCmd = petCmd }));
        else if (petCmd == "UpdateFoodBoxs")
            responses.Add(MakeS2CCall("NCafePetLogic", new { sCmd = petCmd, param = Array.Empty<object>() }));
        else
            responses.Add(MakeS2CCall("NCafePetLogic", new { sCmd = petCmd, param = Array.Empty<object>() }));
        _logger.Info($"lua.cafe_pet account={account} cmd={petCmd}");
    }

    private void HandleGetLastGacha(string account, List<byte[]> responses, LuaCallResult result)
    {
        var player = _repo.Get(account);
        if (player?.Gacha?.Pending == null)
        {
            responses.Add(MakeS2CCall("GetLastGacha", new { err = "error.gacha.nolast" }));
        }
        else
        {
            responses.Add(MakeS2CCall("GetLastGacha", new { bTen = false, bGetCard = false, tbAwards = player.Gacha.Pending }));
        }
        _logger.Info($"lua.get_last_gacha account={account} hasPending={player?.Gacha?.Pending != null}");
    }

    private void HandleSetCustomUp(string account, string method, JsonElement parameters, List<byte[]> responses, LuaCallResult result)
    {
        var customPoolId = parameters.TryGetProperty("nId", out var cpid) ? cpid.GetInt64() : 0;
        var customId1 = parameters.TryGetProperty("nId1", out var ci1) ? ci1.GetInt64() : 0;
        var customId2 = parameters.TryGetProperty("nId2", out var ci2) ? ci2.GetInt64() : 0;
        if (customPoolId > 0)
        {
            _repo.Modify(account, player =>
            {
                var taskGroup = method == "SetCardCustomUp" ? 6L : 4L;
                var packed = (customId1 & 0xffff) | ((customId2 & 0xffff) << 16);
                var customTaskId = (taskGroup << 16) | (uint)(customPoolId + 1);
                player.TaskValues[customTaskId.ToString()] = packed;
            });
        }
        _logger.Info($"lua.set_custom_up account={account} method={method} pool={customPoolId} id1={customId1} id2={customId2}");
        responses.Add(MakeS2CCall(method, new { nId = customPoolId }));
    }

    // ---- GirlLogic ----

    private void HandleGirlLogic(string account, JsonElement p, List<byte[]> responses, LuaCallResult result, string defaultSCmd = "")
    {
        var sCmd = p.TryGetProperty("sCmd", out var cmd) ? cmd.GetString() : defaultSCmd;
        _logger.Info($"lua.girlLogic account={account} sCmd={sCmd}");

        switch (sCmd)
        {
            case "HeadTouched":
                var nId = p.TryGetProperty("nId", out var id) ? id.GetInt64() : 0;
                var nType = p.TryGetProperty("nType", out var t) ? t.GetInt64() : 0;
                _logger.Info($"lua.headTouched account={account} girlId={nId} type={nType}");
                responses.Add(MakeS2CCall("GirlLogic", new { sCmd = "HeadTouched", nId, nType, bSuccess = true }));
                break;

            case "SetMainGirl":
                var mainGirlId = p.TryGetProperty("nId", out var mid) ? mid.GetInt64() : 0;
                if (mainGirlId > 0)
                {
                    _repo.Modify(account, player => { player.TaskValues["131074"] = mainGirlId; });
                }
                _logger.Info($"lua.setMainGirl account={account} girlId={mainGirlId}");
                responses.Add(MakeS2CCall("GirlLogic", new { sCmd = "SetMainGirl", nId = mainGirlId, bSuccess = true }));
                break;

            case "ChangeCloth":
                var clothGirlId = p.TryGetProperty("nId", out var cid) ? cid.GetInt64() : 0;
                var modelId = p.TryGetProperty("nSuit", out var suit) ? suit.GetInt64() : 1;
                if (clothGirlId > 0)
                {
                    _repo.Modify(account, player =>
                    {
                        var girl = player.Girls.FirstOrDefault(g => g.GirlId == clothGirlId);
                        if (girl != null)
                        {
                            girl.ModelId = modelId;
                            var suitOffset = FixGirlModelTaskOffset(modelId);
                            var suitTaskId = MakeGirlTaskId(GIRL_SUIT_TASK_GROUP, clothGirlId, suitOffset);
                            player.TaskValues[suitTaskId.ToString()] = 2;
                        }
                    });
                    // 在 Modify 外部重新获取女孩，避免 EnsureDefaults 修改列表导致引用失效
                    var updatedGirl = _repo.Get(account)?.Girls.FirstOrDefault(g => g.GirlId == clothGirlId);
                    if (updatedGirl != null) result.UpdatedGirls.Add(updatedGirl);
                }
                _logger.Info($"lua.changeCloth account={account} girlId={clothGirlId} modelId={modelId}");
                responses.Add(MakeS2CCall("GirlLogic", new { sCmd = "ChangeCloth", nId = clothGirlId, nSuit = modelId }));
                break;

            case "SetModelInFight":
                var fightGirlId = p.TryGetProperty("nId", out var fid) ? fid.GetInt64() : 0;
                var useModel = p.TryGetProperty("nUse", out var use) ? use.GetInt64() : 0;
                if (fightGirlId > 0)
                {
                    _repo.Modify(account, player =>
                    {
                        if (player.Girls.Any(g => g.GirlId == fightGirlId))
                        {
                            var fightTaskId = MakeGirlTaskId(GIRL_STATE_TASK_GROUP, fightGirlId, GIRL_FIGHT_MODEL_OFFSET);
                            player.TaskValues[fightTaskId.ToString()] = useModel > 0 ? 1 : 0;
                        }
                    });
                }
                _logger.Info($"lua.setModelInFight account={account} girlId={fightGirlId} use={useModel}");
                responses.Add(MakeS2CCall("GirlLogic", new { sCmd = "SetModelInFight", nId = fightGirlId, nUse = useModel, bSuccess = true }));
                break;

            case "GirlGift":
                var giftGirlId = p.TryGetProperty("nId", out var gid) ? gid.GetInt64() : 0;
                var giftItem = p.TryGetProperty("nItem", out var gi) ? gi.GetInt64() : 0;
                var giftCount = p.TryGetProperty("nNum", out var gn) ? gn.GetInt64() : 1;
                _logger.Info($"lua.girlGift account={account} girlId={giftGirlId} item={giftItem} count={giftCount}");
                responses.Add(MakeS2CCall("GirlLogic", new
                {
                    sCmd = "GirlGift", nId = giftGirlId, bLove = true, nIsMaxlevel = 1,
                    nAddExp = 0, nOldExp = 0, nNewExp = 0, nOldLevel = 100, nNewLevel = 100,
                    bAct = false, bSp = false,
                }));
                break;

            case "StartTrain":
                var trainGirlId = p.TryGetProperty("nId", out var tid) ? tid.GetInt64() : 0;
                var trainPos = p.TryGetProperty("nPos", out var tp) ? tp.GetInt64() : 0;
                _logger.Info($"lua.startTrain account={account} girlId={trainGirlId} pos={trainPos}");
                responses.Add(MakeS2CCall("GirlLogic", new { sCmd = "StartTrain", nId = trainGirlId, nPos = trainPos }));
                break;
            case "EndTrain":
                var endTrainGirlId = p.TryGetProperty("nId", out var etid) ? etid.GetInt64() : 0;
                _logger.Info($"lua.endTrain account={account} girlId={endTrainGirlId}");
                responses.Add(MakeS2CCall("GirlLogic", new { sCmd = "EndTrain", nId = endTrainGirlId }));
                break;

            case "LevelAward":
                var awardGirlId = p.TryGetProperty("nId", out var aid) ? aid.GetInt64() : 0;
                var awardLevel = p.TryGetProperty("nLevel", out var al) ? al.GetInt64() : 0;
                _logger.Info($"lua.levelAward account={account} girlId={awardGirlId} level={awardLevel}");
                responses.Add(MakeS2CCall("GirlLogic", new { sCmd = "LevelAward", nId = awardGirlId, nLevel = awardLevel }));
                break;

            default:
                responses.Add(MakeS2CCall("GirlLogic", new { sCmd, bSuccess = true }));
                break;
        }
    }

    // ---- LuaCall（数字 sCmd）----

    private void HandleLuaCall(string account, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        var sCmd = p.TryGetProperty("sCmd", out var cmd) ? cmd.GetInt64() : 0;
        _logger.Info($"lua.luaCall account={account} sCmd={sCmd}");

        switch (sCmd)
        {
            case 1:
                HandleCardDecompose(account, p, responses, result);
                break;
            case 5:
                HandleCardEnhance(account, p, responses, result);
                break;
            case 7:
                // 战力确认：玩家战力低于推荐战力时确认继续战斗
                var levelId7 = p.TryGetProperty("tbParam", out var tp7) && tp7.TryGetProperty("levelid", out var lid) ? lid.GetString() : "";
                var power7 = tp7.TryGetProperty("power", out var pw) ? pw.GetInt64() : 0;
                var required7 = tp7.TryGetProperty("required", out var rq) ? rq.GetInt64() : 0;
                _logger.Info($"lua.power_confirm account={account} level={levelId7} power={power7} required={required7}");
                responses.Add(MakeS2CCall("LuaCall", new { sCmd = 7, tbParam = new { bSuccess = true } }));
                break;
            case 50:
                HandleBountyJoin(account, p, responses, result);
                break;
            case 51:
                HandleBountyPass(account, p, responses, result);
                break;
            case 52:
                HandleBountyFail(account, p, responses, result);
                break;
            case 11:
                var shopId = p.TryGetProperty("tbParam", out var shopP) && shopP.TryGetProperty("shopid", out var sid) ? sid.GetInt64() : 0;
                _logger.Info($"lua.shop_goods_list account={account} shopid={shopId}");
                responses.Add(MakeS2CCall("LuaCall", new { sCmd = 11, tbParam = new { shopid = shopId, isopen = 1, refreshcount = 0, goodslist = Array.Empty<object>() } }));
                break;
            case 61:
                responses.Add(MakeS2CCall("LuaCall", new { sCmd, tbParam = new { nType = 1, tbGoods = Array.Empty<object>() } }));
                break;
            case 21:
            case 22:
            case 23:
                HandleFormationUpdate(account, p, responses, result);
                break;
            case 252:
                HandleWrappedCall(account, p, responses, result);
                break;
            case 102:
                HandleGuideLog(account, p, responses, result);
                break;
            case 71:
                responses.Add(MakeS2CCall("LuaCall", new { sCmd = 71, tbParam = new { nType = 1, bSuccess = true, nDay = 1, tbAward = Array.Empty<object>() } }));
                break;
            case 73:
                responses.Add(MakeS2CCall("LuaCall", new { sCmd = 73, tbParam = new { nType = 1, bSuccess = true, tbAward = Array.Empty<object>() } }));
                break;
            case 77:
                responses.Add(MakeS2CCall("LuaCall", new { sCmd = 77, tbParam = new { nType = 2, bSuccess = true, nDay = 8, tbAward = Array.Empty<object>() } }));
                break;
            case 74:
                HandleSetSkin(account, p, responses, result);
                break;
            case 80:
                HandleFriendList(p, responses, result);
                break;
            case 84:
                HandleFriendAnswer(p, responses, result, account);
                break;
            case 90:
                HandleRoleSearch(account, p, responses, result);
                break;
            case 94:
                HandleVisitingCard(account, responses, result);
                break;
            case 170:
                responses.Add(MakeS2CCall("LuaCall", new { sCmd = 170, tbParam = new { } }));
                break;
            case 202:
                responses.Add(MakeS2CCall("LuaCall", new { sCmd = 202, tbParam = new { Result = 0 } }));
                break;
            case 230:
                responses.Add(MakeS2CCall("LuaCall", new { sCmd = 230, tbParam = new { PromiseIsOpen = false } }));
                break;
            case 231:
                responses.Add(MakeS2CCall("LuaCall", new { sCmd = 231, tbParam = Array.Empty<object>() }));
                break;
            case 250:
                HandleAssistList(responses, result);
                break;
            case 10003:
                HandleClubSearch(account, p, responses, result);
                break;
            case 10004:
                HandleClubJoin(p, responses, result, account);
                break;
            case 10000:
                HandleRecharge(account, p, responses, result);
                break;
            case 112:
                HandleCafeData(account, p, responses, result);
                break;
            case 113:
                // 设置服务员列表：存储到玩家状态并回显
                JsonElement? waiterRaw = p.TryGetProperty("tbParam", out var wl) && wl.ValueKind == JsonValueKind.Array ? wl : null;
                var waitersToStore = new List<long[]> { new long[0], new long[0], new long[0] };
                if (waiterRaw.HasValue)
                {
                    var arr = waiterRaw.Value;
                    for (int i = 0; i < Math.Min(3, arr.GetArrayLength()); i++)
                    {
                        var area = arr[i];
                        if (area.ValueKind == JsonValueKind.Array)
                        {
                            var ids = new List<long>();
                            foreach (var item in area.EnumerateArray())
                            {
                                if (item.TryGetProperty("girlid", out var gid)) ids.Add(gid.GetInt64());
                                else if (item.ValueKind == JsonValueKind.Number) ids.Add(item.GetInt64());
                            }
                            waitersToStore[i] = ids.ToArray();
                        }
                    }
                }
                _repo.Modify(account, player =>
                {
                    player.Cafe ??= new CafeState();
                    player.Cafe.Waiters = waitersToStore;
                });
                var waiterEcho = waitersToStore.Select(w => (object)w.Select(id => new { girlid = id }).ToArray()).ToArray();
                responses.Add(MakeS2CCall("LuaCall", new { sCmd = 113, tbParam = waiterEcho }));
                _logger.Info($"lua.set_waiters account={account} areas={waitersToStore.Count}");
                break;
            case 115:
                // 生成顾客：存储到玩家状态
                var now115 = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var newCustomer = new CafeCustomer { CustomerType = 201, CustomerIdx = 1, StartTime = now115 };
                _repo.Modify(account, player =>
                {
                    player.Cafe ??= new CafeState();
                    player.Cafe.Customers ??= new List<CafeCustomer>();
                    player.Cafe.Customers.Add(newCustomer);
                    player.Cafe.LastCustomerTime = now115;
                });
                responses.Add(MakeS2CCall("LuaCall", new { sCmd = 115, tbParam = new { basetime = now115, customerqueue = new[] { new { customertype = 201, customeridx = 1, starttime = now115 } } } }));
                break;
            case 119:
                // 制作咖啡
                HandleMakeCoffee(account, p, responses, result);
                break;
            case 124:
                // 添加顾客权重：返回空
                responses.Add(MakeS2CCall("LuaCall", new { sCmd = 124, tbParam = Array.Empty<object>() }));
                break;
            case 241:
                // 家具数量：统计玩家 genre=10 的家具物品
                var player241 = _repo.Get(account);
                int furnitureCount = player241?.Inventory.Count(i => i.Genre == 10) ?? 0;
                responses.Add(MakeS2CCall("LuaCall", new { sCmd = 241, tbParam = new { nRet = 0, nNum = furnitureCount } }));
                _logger.Info($"lua.furniture_count account={account} count={furnitureCount}");
                break;
            case 91:
                var param91 = p.TryGetProperty("tbParam", out var tp91) ? tp91.ToString() : "0";
                _logger.Info($"lua.sCmd91 account={account} param={param91}");
                responses.Add(MakeS2CCall("LuaCall", new { sCmd = 91, tbParam = new { } }));
                break;
            case 540:
                HandleMainChapterEnter(account, p, responses, result);
                break;
            default:
                var paramJson = p.GetRawText();
                _logger.Info($"lua.luaCall.unimplemented sCmd={sCmd} params={paramJson}");
                break;
        }
    }

    private void HandleWrappedCall(string account, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        if (!p.TryGetProperty("tbParam", out var inner))
        {
            responses.Add(MakeS2CCall("LuaCall", new { sCmd = 252, bSuccess = true }));
            return;
        }

        // 内层 sCmd 可能是数字（悬赏战斗 50/51/52）或字符串（ChapterMsg）
        int innerNumCmd = 0;
        string innerStrCmd = "";
        if (inner.TryGetProperty("sCmd", out var ic))
        {
            if (ic.ValueKind == JsonValueKind.Number) innerNumCmd = ic.GetInt32();
            else innerStrCmd = ic.GetString() ?? "";
        }

        if (innerStrCmd == "ChapterMsg")
        {
            if (inner.TryGetProperty("tbParam", out var chapterParam))
                HandleChapterMsg(account, chapterParam, responses, result);
            else
                responses.Add(MakeS2CCall("LuaCall", new { sCmd = 252, bSuccess = true }));
            return;
        }

        // 悬赏战斗：50=JOIN, 51=PASS, 52=FAIL
        if (innerNumCmd is 50 or 51 or 52)
        {
            var bountyParam = inner.TryGetProperty("tbParam", out var bp) ? bp : inner;
            switch (innerNumCmd)
            {
                case 50: HandleBountyJoin(account, bountyParam, responses, result); break;
                case 51: HandleBountyPass(account, bountyParam, responses, result); break;
                case 52: HandleBountyFail(account, bountyParam, responses, result); break;
            }
            return;
        }

        responses.Add(MakeS2CCall("LuaCall", new { sCmd = 252, bSuccess = true }));
    }

    // ---- 悬赏战斗 ----

    private void HandleBountyJoin(string account, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        try
        {
            var activityId = p.TryGetProperty("id", out var id) ? id.GetInt32() : (p.TryGetProperty("nActivityId", out var aid) ? aid.GetInt32() : 0);
            var difficulty = p.TryGetProperty("diff", out var diff) ? diff.GetInt32() : (p.TryGetProperty("nDiff", out var nd) ? nd.GetInt32() : 1);
            var formationId = p.TryGetProperty("nFormationId", out var fid) ? fid.GetInt32() : 1;
            _logger.Info($"lua.bounty.join account={account} activity={activityId} diff={difficulty} formation={formationId}");

            var level = BountyData.GetLevel(activityId, difficulty);
            if (level == null)
            {
                _logger.Warn($"bounty.level.missing account={account} activity={activityId} diff={difficulty}");
                responses.Add(MakeS2CCall("LuaCall", new { sCmd = 50, nError = 2 }));
                return;
            }

            // 检查体力
            var player = _repo.Get(account);
            var energyCost = level.EnergyCost;
            var vigour = player?.Money.FirstOrDefault(m => m.Id == 1)?.Count ?? 0;
            if (vigour < energyCost)
            {
                _logger.Info($"bounty.enter.insufficient_vigour account={account} required={energyCost} available={vigour}");
                responses.Add(MakeS2CCall("LuaCall", new { sCmd = 50, nError = 5 }));
                return;
            }

            // 扣除体力
            _repo.Modify(account, p =>
            {
                var vig = p.Money.FirstOrDefault(m => m.Id == 1);
                if (vig != null && vig.Count >= energyCost)
                {
                    vig.Count -= energyCost;
                    result.UpdatedMoney.Add(vig);
                }
            });

            // 预生成掉落奖励（客户端战斗中显示）
            var rng = new Random();
            var dropItems = new List<object>();
            for (int i = 0; i < rng.Next(2, 5); i++)
            {
                dropItems.Add(new
                {
                    Genre = 7,
                    Detail = 3,
                    Particular = 1,
                    TemplateLevel = rng.Next(1, 5),
                    Count = rng.Next(1, 4),
                });
            }

            // 返回完整格式，让客户端进入战斗（直接 sCmd=50，不包装在 252 里）
            responses.Add(MakeS2CCall("LuaCall", new
            {
                sCmd = 50,
                nError = 0,
                tbData = new { id = activityId, diff = difficulty },
                tbDrop = Array.Empty<object>(),
                tbDropItems = dropItems.ToArray(),
                tbSpeThiefDrop = Array.Empty<object>(),
            }));
            _logger.Info($"bounty.joined account={account} activity={activityId} diff={difficulty} energy={energyCost}");
        }
        catch (Exception ex)
        {
            _logger.Error($"bounty.join.error {ex.Message}");
            responses.Add(MakeS2CCall("LuaCall", new { sCmd = 50, nError = 1 }));
        }
    }

    private void HandleBountyPass(string account, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        try
        {
            var activityId = p.TryGetProperty("nActivityId", out var aid) ? aid.GetInt32() : (p.TryGetProperty("id", out var id) ? id.GetInt32() : 0);
            var difficulty = p.TryGetProperty("nDiff", out var nd) ? nd.GetInt32() : (p.TryGetProperty("diff", out var diff) ? diff.GetInt32() : 1);
            var formationId = p.TryGetProperty("nFormationId", out var fid) ? fid.GetInt32() : 1;
            _logger.Info($"lua.bounty.pass account={account} activity={activityId} diff={difficulty}");

            var level = BountyData.GetLevel(activityId, difficulty);

            // 结算奖励
            var rng = new Random();
            var awards = new List<object>();
            var goldReward = 500 + difficulty * 200;
            var expReward = 100 + difficulty * 50;

            _repo.Modify(account, player =>
            {
                // 金币
                var gold = player.Money.FirstOrDefault(m => m.Id == 2);
                if (gold != null) { gold.Count += goldReward; result.UpdatedMoney.Add(gold); }
                // 钻石
                var diamond = player.Money.FirstOrDefault(m => m.Id == 3);
                if (diamond != null) { diamond.Count += rng.Next(10, 30); result.UpdatedMoney.Add(diamond); }
                // 玩家经验
                var (newLevel, newExp, levelsGained) = PlayerLevelData.AddExperience((int)player.Level, player.Exp, expReward);
                if (levelsGained > 0 || newExp != player.Exp)
                {
                    player.Level = newLevel;
                    player.Exp = newExp;
                    result.ExperienceChanged = true;
                }
                // 女孩经验
                foreach (var girl in player.Girls)
                {
                    girl.Exp += 50;
                    result.UpdatedGirls.Add(girl);
                }
                // 掉落材料
                for (int i = 0; i < rng.Next(1, 4); i++)
                {
                    var matGuid = player.NextItemGuid++;
                    player.Inventory.Add(new InventoryEntry
                    {
                        Guid = matGuid, Genre = 7, Detail = 3, Particular = 1,
                        TemplateLevel = rng.Next(1, 5), Count = rng.Next(1, 5),
                        CreateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        EnhanceLevel = 1,
                    });
                    result.UpdatedItems.Add(player.Inventory.Last());
                }
                // 悬赏通过任务
                if (level != null)
                {
                    var passTaskId = BountyData.MakePassTaskId(activityId);
                    player.TaskValues[passTaskId.ToString()] = Math.Max(difficulty, player.TaskValues.TryGetValue(passTaskId.ToString(), out var v) ? (int)v : 0);
                    var dailyTaskId = BountyData.MakeDailyTaskId(level.EventType);
                    player.TaskValues[dailyTaskId.ToString()] = (player.TaskValues.TryGetValue(dailyTaskId.ToString(), out var dv) ? (int)dv : 0) + 1;
                }
                result.NeedsPlayerSync = true;
            });

            responses.Add(MakeS2CCall("LuaCall", new
            {
                sCmd = 252,
                tbParam = new
                {
                    sCmd = 51,
                    nError = 0,
                    tbAwards = awards.ToArray(),
                    tbExp = new { MasterExp = expReward, CardExp = 50 },
                }
            }));
            _logger.Info($"bounty.passed account={account} activity={activityId} diff={difficulty} gold={goldReward}");
        }
        catch (Exception ex)
        {
            _logger.Error($"bounty.pass.error {ex.Message}");
            responses.Add(MakeS2CCall("LuaCall", new { sCmd = 51, nError = 1 }));
        }
    }

    private void HandleBountyFail(string account, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        var activityId = p.TryGetProperty("nActivityId", out var aid) ? aid.GetInt32() : 0;
        var difficulty = p.TryGetProperty("nDiff", out var nd) ? nd.GetInt32() : 1;
        _logger.Info($"lua.bounty.fail account={account} activity={activityId} diff={difficulty}");
        responses.Add(MakeS2CCall("LuaCall", new { sCmd = 52, nError = 0 }));
    }

    private void HandleSetSkin(string account, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        int avatarFrameId = 0, displayFrameId = 0, chatBubbleId = 0;
        if (p.TryGetProperty("tbParam", out var tbRaw) && tbRaw.ValueKind == JsonValueKind.Array && tbRaw.GetArrayLength() >= 3)
        {
            avatarFrameId = tbRaw[0].GetInt32();
            displayFrameId = tbRaw[1].GetInt32();
            chatBubbleId = tbRaw[2].GetInt32();
        }
        _logger.Info($"lua.set_skin account={account} avatar={avatarFrameId} display={displayFrameId} bubble={chatBubbleId}");
        _repo.Modify(account, player =>
        {
            if (avatarFrameId >= 0) player.TaskValues[((50L << 16) | 1).ToString()] = avatarFrameId;
            if (displayFrameId >= 0) player.TaskValues[((50L << 16) | 2).ToString()] = displayFrameId;
            if (chatBubbleId >= 0) player.TaskValues[((50L << 16) | 3).ToString()] = chatBubbleId;
        });
        responses.Add(MakeS2CCall("LuaCall", new { sCmd = 74, tbParam = new { nRet = 0 } }));
    }

    private void HandleFriendList(JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        var friendType = p.TryGetProperty("tbParam", out var fp) && fp.TryGetProperty("reqfriendtype", out var ft) ? ft.GetInt64() : 1;
        var rng = new Random(42);
        var girlIds = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
        var names = new[] { "星野", "爱丽丝", "白子", "晴奈", "优香", "日富美", "梓", "纱织", "花子", "春香", "美游", "野乃美", "和香", "枫", "千夏", "明日奈" };
        var hyList = new List<object>();
        for (int i = 0; i < 12; i++)
        {
            var gid = girlIds[i % girlIds.Length];
            hyList.Add(new
            {
                roleid = 10000 + i, name = names[i % names.Length], level = 70 + rng.Next(30),
                girlid = gid, modelid = 1 + rng.Next(5), power = 50000 + rng.Next(50000),
                lastlogin = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - rng.Next(86400),
                online = rng.Next(2) == 1 ? 1 : 0,
            });
        }
        var fxList = new List<object>();
        for (int i = 12; i < 16; i++)
        {
            var gid = girlIds[i % girlIds.Length];
            fxList.Add(new
            {
                roleid = 20000 + i, name = names[i % names.Length], level = 60 + rng.Next(40),
                girlid = gid, modelid = 1 + rng.Next(3), power = 30000 + rng.Next(40000),
            });
        }
        responses.Add(MakeS2CCall("LuaCall", new
        {
            sCmd = 80,
            tbParam = new
            {
                reqfriendtype = friendType, HYList = hyList.ToArray(), FXList = fxList.ToArray(),
                SQList = Array.Empty<object>(), HMDList = Array.Empty<object>(), BindList = Array.Empty<object>(),
            }
        }));
    }

    private void HandleFriendAnswer(JsonElement p, List<byte[]> responses, LuaCallResult result, string account)
    {
        var answerRoleId = 0L;
        var askRoleId = 0L;
        if (p.TryGetProperty("tbParam", out var ap))
        {
            if (ap.TryGetProperty("answerroleid", out var ar)) answerRoleId = ar.GetInt64();
            if (ap.TryGetProperty("askroleid", out var ak)) askRoleId = ak.GetInt64();
        }
        _logger.Info($"lua.friend_answer account={account} answerRoleId={answerRoleId} askRoleId={askRoleId}");
        responses.Add(MakeS2CCall("LuaCall", new { sCmd = 84, tbParam = new { bSuccess = true } }));
    }

    private void HandleRoleSearch(string account, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        var roleId = 0L;
        if (p.TryGetProperty("tbParam", out var rp))
        {
            if (rp.TryGetProperty("roleid", out var rid)) roleId = rid.GetInt64();
        }
        _logger.Info($"lua.role_search account={account} roleid={roleId}");

        var gmResult = "";
        var gmName = "GM工具";
        switch (roleId)
        {
            case 1111: gmName = "GM:11111等级80,11112体力,11113金币,11114青辉石,11115解锁,11116全角色,11117全角色满级,11375一键满配"; break;
            case 11111: gmResult = ExecuteGmCommand(account, "level 80"); gmName = "等级80 OK"; break;
            case 11112: gmResult = ExecuteGmCommand(account, "vigor 999"); gmName = "体力999 OK"; break;
            case 11113: gmResult = ExecuteGmCommand(account, "gold 9999999"); gmName = "金币999万 OK"; break;
            case 11114: gmResult = ExecuteGmCommand(account, "diamond 99999"); gmName = "青辉石99999 OK"; break;
            case 11115: gmResult = ExecuteGmCommand(account, "unlockall"); gmName = "解锁全部 OK"; break;
            case 11116: gmResult = ExecuteGmCommand(account, "maxcards"); gmName = "全角色 OK"; break;
            case 11117: gmResult = ExecuteGmCommand(account, "maxlevel"); gmName = "全角色满级 OK"; break;
            case 11375:
                ExecuteGmCommand(account, "level 80");
                ExecuteGmCommand(account, "vigor 999");
                ExecuteGmCommand(account, "gold 9999999");
                ExecuteGmCommand(account, "diamond 99999");
                ExecuteGmCommand(account, "maxmoney");
                ExecuteGmCommand(account, "unlockall");
                ExecuteGmCommand(account, "maxcards");
                ExecuteGmCommand(account, "maxlevel");
                ExecuteGmCommand(account, "allitems");
                gmResult = "全部GM命令已执行（含全货币+全物品）";
                gmName = "一键满配 OK";
                break;
            default: gmName = $"角色{roleId}"; break;
        }
        if (!string.IsNullOrEmpty(gmResult)) _logger.Info($"lua.gm_via_friend account={account} roleid={roleId} result={gmResult}");

        responses.Add(MakeS2CCall("LuaCall", new
        {
            sCmd = 90,
            tbParam = new { roleid = roleId, name = gmName, level = 80, clubname = "GM俱乐部", bSuccess = true }
        }));
    }

    private void HandleVisitingCard(string account, List<byte[]> responses, LuaCallResult result)
    {
        var player = _repo.Get(account);
        int avatarFrameId = 0, displayFrameId = 0, chatBubbleId = 0;
        if (player != null)
        {
            if (player.TaskValues.TryGetValue(((50L << 16) | 1).ToString(), out var af)) avatarFrameId = (int)af;
            if (player.TaskValues.TryGetValue(((50L << 16) | 2).ToString(), out var df)) displayFrameId = (int)df;
            if (player.TaskValues.TryGetValue(((50L << 16) | 3).ToString(), out var cb)) chatBubbleId = (int)cb;
        }
        responses.Add(MakeS2CCall("LuaCall", new
        {
            sCmd = 94,
            tbParam = new { VisitingCardID = displayFrameId, PlayerListSkinID = avatarFrameId, ChatBubbleID = chatBubbleId }
        }));
    }

    private void HandleAssistList(List<byte[]> responses, LuaCallResult result)
    {
        var rng = new Random(42);
        var girlIds = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        var names = new[] { "星野", "爱丽丝", "白子", "晴奈", "优香", "日富美", "梓", "纱织", "花子", "春香" };
        var assistList = new List<object>();
        for (int i = 0; i < 8; i++)
        {
            var gid = girlIds[i % girlIds.Length];
            assistList.Add(new
            {
                roleid = 10000 + i, name = names[i % names.Length], level = 75 + rng.Next(25),
                girlid = gid, modelid = 1 + rng.Next(5), weaponid = 1001 + i,
                power = 60000 + rng.Next(40000), cardlist = new[] { gid * 100 + 1, gid * 100 + 2 },
            });
        }
        responses.Add(MakeS2CCall("LuaCall", new { sCmd = 250, tbParam = assistList.ToArray() }));
    }

    private void HandleClubSearch(string account, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        var searchText = "";
        if (p.TryGetProperty("tbParam", out var sp))
        {
            foreach (var prop in sp.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    searchText = prop.Value.GetString() ?? "";
                    if (!string.IsNullOrEmpty(searchText)) break;
                }
            }
        }
        _logger.Info($"lua.club_search account={account} text={searchText}");

        if (!string.IsNullOrEmpty(searchText) && searchText.StartsWith("!"))
        {
            var gmResult = ExecuteGmCommand(account, searchText[1..]);
            _logger.Info($"lua.gm_via_club account={account} cmd={searchText} result={gmResult}");
        }

        responses.Add(MakeS2CCall("LuaCall", new
        {
            sCmd = 10003,
            tbParam = new
            {
                clubList = new[]
                {
                    new { clubid = 114514, name = "GM工具(输入!命令)", level = 1, memberCount = 1, masterName = "System" }
                }
            }
        }));
    }

    private void HandleClubJoin(JsonElement p, List<byte[]> responses, LuaCallResult result, string account)
    {
        var clubId = 0L;
        if (p.TryGetProperty("tbParam", out var cp))
        {
            if (cp.TryGetProperty("clubid", out var cid)) clubId = cid.GetInt64();
        }
        _logger.Info($"lua.club_join account={account} clubid={clubId}");
        responses.Add(MakeS2CCall("LuaCall", new { sCmd = 10004, tbParam = new { bSuccess = true, clubid = clubId } }));
    }

    private void HandleRecharge(string account, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        var itemId = 0;
        if (p.TryGetProperty("tbParam", out var rp))
        {
            if (rp.TryGetProperty("nId", out var nid)) itemId = nid.GetInt32();
        }
        _logger.Info($"lua.do_recharge account={account} itemId={itemId}");

        _repo.Modify(account, player =>
        {
            if (itemId == 12)
            {
                var giftGuid = player.Inventory.Count > 0 ? player.Inventory.Max(i => i.Guid) + 1 : 10001;
                var giftItem = new InventoryEntry
                {
                    Guid = giftGuid, Genre = 14, Detail = 3, Particular = 5,
                    TemplateLevel = 1, Count = 1, CreateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    EnhanceLevel = 0, EnhanceExp = 0, BreakLevel = 0, LockOn = 0,
                };
                player.Inventory.Add(giftItem);
                result.UpdatedItems.Add(giftItem);
                responses.Add(MakeS2CCall("LuaCall", new { sCmd = 10000, tbParam = new { Id = 12, tbItem = new[] { new[] { 14, 3, 5, 1, 1 } } } }));
            }
            else if (itemId == 1)
            {
                var diamond = player.Money.FirstOrDefault(m => m.Id == 3);
                if (diamond != null)
                {
                    diamond.Count += 300;
                    result.UpdatedMoney.Add(diamond);
                }
                var monthCardTaskId = ((1L << 16) | 36).ToString();
                var expireTime = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
                player.TaskValues[monthCardTaskId] = expireTime;
                result.ExperienceChanged = true;
                responses.Add(MakeS2CCall("LuaCall", new { sCmd = 10000, tbParam = new { Id = 1, bSuccess = true } }));
            }
            else
            {
                responses.Add(MakeS2CCall("LuaCall", new { sCmd = 10000, tbParam = new { Id = itemId, bSuccess = true } }));
            }
        });
    }

    // ---- 编队更新 ----

    private void HandleFormationUpdate(string account, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        try
        {
            if (!p.TryGetProperty("tbParam", out var tbParam))
            {
                responses.Add(MakeS2CCall("LuaCall", new { sCmd = 21, bSuccess = false }));
                return;
            }

            var formationId = tbParam.TryGetProperty("Id", out var idEl) ? idEl.GetInt64() : 1;

            var cards = new List<FightCardState>();
            if (tbParam.TryGetProperty("Info", out var infoArr) && infoArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var cardEl in infoArr.EnumerateArray())
                {
                    long GetLong(JsonElement el, string name)
                    {
                        if (el.TryGetProperty(name, out var v)) return v.GetInt64();
                        if (el.TryGetProperty(char.ToLower(name[0]) + name.Substring(1), out var v2)) return v2.GetInt64();
                        return 0;
                    }
                    var card = new FightCardState
                    {
                        MainCardGuid = GetLong(cardEl, "MainCard"),
                        UsedCardGuid = GetLong(cardEl, "UsedCard"),
                        WeaponGuid = GetLong(cardEl, "WeaponId"),
                        SecondaryCardGuids = new List<long>(),
                        RuneItemGuids = new List<long>(),
                    };
                    if (card.UsedCardGuid == 0 && card.MainCardGuid > 0)
                        card.UsedCardGuid = card.MainCardGuid;
                    JsonElement GetArr(JsonElement el, string name)
                    {
                        if (el.TryGetProperty(name, out var v)) return v;
                        if (el.TryGetProperty(char.ToLower(name[0]) + name.Substring(1), out var v2)) return v2;
                        return default;
                    }
                    var scArr = GetArr(cardEl, "Secondarycard");
                    if (scArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var g in scArr.EnumerateArray())
                            card.SecondaryCardGuids.Add(g.GetInt64());
                    }
                    var ruArr = GetArr(cardEl, "Rune");
                    if (ruArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var g in ruArr.EnumerateArray())
                            card.RuneItemGuids.Add(g.GetInt64());
                    }
                    cards.Add(card);
                }
            }

            _repo.Modify(account, player =>
            {
                var formation = player.Formations.FirstOrDefault(f => f.Id == formationId);
                if (formation != null)
                {
                    formation.FightCards = cards;
                }
                else
                {
                    player.Formations.Add(new FormationState { Id = formationId, Title = $"编队{formationId}", FightCards = cards });
                }
                result.Formation = player.Formations.First(f => f.Id == formationId);
            });

            _logger.Info($"lua.formation_update account={account} formationId={formationId} cards={cards.Count}");
            responses.Add(MakeS2CCall("LuaCall", new { sCmd = 21, tbParam = new { ret = 0 } }));
        }
        catch (Exception ex)
        {
            _logger.Error($"lua.formation_error {ex.Message}");
            responses.Add(MakeS2CCall("LuaCall", new { sCmd = 21, bSuccess = false }));
        }
    }

    // ---- 引导日志 ----

    private void HandleGuideLog(string account, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        try
        {
            var tbParam = p.TryGetProperty("tbParam", out var tp) ? tp : default;
            var nTimming = tbParam.TryGetProperty("nTimming", out var nt) ? nt.GetInt64() : 0;
            var guideId = tbParam.TryGetProperty("GuideID", out var gid) ? gid.GetInt64() : 0;
            var stepId = tbParam.TryGetProperty("StepID", out var sid) ? sid.GetInt64() : 0;
            var guideType = tbParam.TryGetProperty("GuideType", out var gt) ? gt.GetString() : "Force";

            _logger.Info($"lua.guide_log account={account} GuideId={guideId} StepId={stepId} Type={guideType}");

            responses.Add(MakeS2CCall("LuaCall", new
            {
                sCmd = 102,
                tbParam = new { nTimming, GuideId = guideId, StepId = stepId, GuideType = guideType, }
            }));
        }
        catch (Exception ex)
        {
            _logger.Error($"lua.guide_log_error {ex.Message}");
            responses.Add(MakeS2CCall("LuaCall", new { sCmd = 102, tbParam = new { } }));
        }
    }

    // ---- 咖啡馆数据 ----

    private void HandleCafeData(string account, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var player = _repo.Get(account);

        // 咖啡列表：使用玩家真实库存
        var coffeeList = player?.Cafe?.Coffees?
            .Select(c => new { coffeetype = c.CoffeeType, count = c.Count })
            .ToArray() ?? Array.Empty<object>();

        // 服务员列表：使用玩家存储的3个区域
        var waiters = player?.Cafe?.Waiters ?? new List<long[]> { new long[0], new long[0], new long[0] };
        var roomGirlsList = waiters.Select(w => (object)w.Select(id => new { girlid = id }).ToArray()).ToArray();

        // 顾客队列：使用玩家存储的，空则生成一个
        var customers = player?.Cafe?.Customers?
            .Select(c => (object)new { customertype = c.CustomerType, customeridx = c.CustomerIdx, starttime = c.StartTime })
            .ToList();
        if (customers == null || customers.Count == 0)
        {
            customers = new List<object> { new { customertype = 201, customeridx = 1, starttime = now } };
        }

        var cafeData = new
        {
            basetime = now, level = 10, hot = 9999, comfort = 9999,
            seatlist = Array.Empty<object>(),
            customerqueue = customers.ToArray(),
            coffeelist = coffeeList,
            roomgirlslist = roomGirlsList,
            weightlist = Array.Empty<object>(), visitedList = Array.Empty<object>(),
            boxstatelist = Array.Empty<object>(), petstatelist = Array.Empty<object>(),
            petlocklist = Array.Empty<object>(), nextpetid = 2,
        };
        responses.Add(MakeS2CCall("LuaCall", new { sCmd = 112, tbParam = cafeData }));
        _logger.Info($"lua.cafe_data account={account} coffees={coffeeList.Length} waiters={waiters.Count}");
    }

    // ---- 制作咖啡 ----

    private void HandleMakeCoffee(string account, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        var coffeeType = p.TryGetProperty("coffeetype", out var ct) ? ct.GetInt32() : 0;
        var count = p.TryGetProperty("count", out var cn) ? cn.GetInt32() : 1;
        if (coffeeType <= 0 || count <= 0)
        {
            responses.Add(MakeS2CCall("LuaCall", new { sCmd = 119, tbParam = new { coffeelist = Array.Empty<object>() } }));
            return;
        }

        _repo.Modify(account, player =>
        {
            player.Cafe ??= new CafeState();
            player.Cafe.Coffees ??= new List<CafeCoffee>();
            var existing = player.Cafe.Coffees.FirstOrDefault(c => c.CoffeeType == coffeeType);
            if (existing != null) existing.Count += count;
            else player.Cafe.Coffees.Add(new CafeCoffee { CoffeeType = coffeeType, Count = count });
        });

        var player = _repo.Get(account);
        var coffeeList = player?.Cafe?.Coffees?
            .Select(c => new { coffeetype = c.CoffeeType, count = c.Count })
            .ToArray() ?? Array.Empty<object>();
        responses.Add(MakeS2CCall("LuaCall", new { sCmd = 119, tbParam = new { coffeelist = coffeeList } }));
        _logger.Info($"lua.make_coffee account={account} type={coffeeType} count={count}");
    }

    // ---- 章节战斗 ----

    // ---- 主线关卡进入（LuaCall sCmd=540）----

    private void HandleMainChapterEnter(string account, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        try
        {
            var tbParam = p.TryGetProperty("tbParam", out var tp) ? tp : p;
            var chapter = tbParam.TryGetProperty("Chapter", out var ch) ? ch.GetInt64() : 0;
            var index = tbParam.TryGetProperty("Index", out var idx) ? idx.GetInt64() : 0;
            var difficult = tbParam.TryGetProperty("Difficult", out var diff) ? diff.GetInt64() : 0;
            var formationId = tbParam.TryGetProperty("nFormationId", out var fid) ? fid.GetInt64() : 1;
            _logger.Info($"lua.main_chapter_enter account={account} chapter={chapter} index={index} diff={difficult} formation={formationId}");

            // 检查关卡配置（Difficult=0 回退到 1）
            var levelConfig = ChapterConfig.Get((int)chapter, (int)index, (int)difficult);
            if (levelConfig == null && difficult == 0)
            {
                levelConfig = ChapterConfig.Get((int)chapter, (int)index, 1);
                if (levelConfig != null)
                    _logger.Info($"lua.main_chapter_enter.diff_fallback account={account} chapter={chapter} index={index} 0→1");
            }

            if (levelConfig == null)
            {
                _logger.Warn($"main_chapter.level.missing account={account} chapter={chapter} index={index} diff={difficult}");
                responses.Add(MakeS2CCall("ChapterMsg", new { nError = 1, nState = 0 }));
                return;
            }

            // 检查体力
            var player = _repo.Get(account);
            var energyCost = ChapterConfig.EffectiveEnergyCost(levelConfig, (int)(player?.Level ?? 1));
            var vigour = player?.Money.FirstOrDefault(m => m.Id == 1)?.Count ?? 0;
            if (vigour < energyCost)
            {
                _logger.Info($"main_chapter.enter.insufficient_vigour account={account} required={energyCost} available={vigour}");
                responses.Add(MakeS2CCall("ChapterMsg", new { nError = 20014, nState = 0 }));
                return;
            }

            // 扣除体力
            _repo.Modify(account, pl =>
            {
                var vig = pl.Money.FirstOrDefault(m => m.Id == 1);
                if (vig != null && vig.Count >= energyCost)
                {
                    vig.Count -= energyCost;
                    result.UpdatedMoney.Add(vig);
                }
            });

            // 返回进入战斗响应（使用 ChapterMsg 格式，客户端本地加载战斗场景）
            responses.Add(MakeS2CCall("ChapterMsg", new
            {
                nError = 0,
                nState = 0,
                tbEnter = Array.Empty<object>(),
                tbDropItems = Array.Empty<object>(),
            }));
            _logger.Info($"main_chapter.entered account={account} chapter={chapter} index={index} energy={energyCost}");
        }
        catch (Exception ex)
        {
            _logger.Error($"main_chapter.enter.error {ex.Message}");
            responses.Add(MakeS2CCall("ChapterMsg", new { nError = 1, nState = 0 }));
        }
    }

    private void HandleChapterMsg(string account, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        var nState = p.TryGetProperty("nState", out var st) ? st.GetInt64() : 0;
        var chapter = p.TryGetProperty("Chapter", out var ch) ? ch.GetInt64() : 0;
        var index = p.TryGetProperty("Index", out var idx) ? idx.GetInt64() : 0;
        var difficult = p.TryGetProperty("Difficult", out var diff) ? diff.GetInt64() : 0;
        _logger.Info($"lua.chapterMsg account={account} nState={nState} chapter={chapter} index={index} diff={difficult}");

        // 检查关卡配置是否存在
        // 客户端可能发送 Difficult=0 或不发该字段，回退到 Difficult=1
        var levelConfig = ChapterConfig.Get((int)chapter, (int)index, (int)difficult);
        if (levelConfig == null && difficult == 0)
        {
            levelConfig = ChapterConfig.Get((int)chapter, (int)index, 1);
            if (levelConfig != null)
                _logger.Info($"lua.chapterMsg.diff_fallback account={account} chapter={chapter} index={index} 0→1");
        }

        switch (nState)
        {
            case 0:
                if (levelConfig == null)
                {
                    _logger.Warn($"chapter.level.missing account={account} chapter={chapter} index={index} diff={difficult}");
                    responses.Add(MakeS2CCall("ChapterMsg", new { nError = 1, nState = 0 }));
                    return;
                }
                // 检查体力
                var player = _repo.Get(account);
                var energyCost = ChapterConfig.EffectiveEnergyCost(levelConfig, (int)(player?.Level ?? 1));
                var vigour = player?.Money.FirstOrDefault(m => m.Id == 1)?.Count ?? 0;
                if (vigour < energyCost)
                {
                    _logger.Info($"chapter.enter.insufficient_vigour account={account} required={energyCost} available={vigour}");
                    responses.Add(MakeS2CCall("ChapterMsg", new { nError = 20014, nState = 0 }));
                    return;
                }
                responses.Add(MakeS2CCall("ChapterMsg", new { nError = 0, nState = 0, tbEnter = Array.Empty<object>(), tbDropItems = Array.Empty<object>() }));
                break;
            case 1:
                if (levelConfig == null)
                {
                    _logger.Warn($"chapter.settlement.unknown account={account} chapter={chapter} index={index} diff={difficult}");
                    responses.Add(MakeS2CCall("ChapterMsg", new { nError = 1, nState = 1 }));
                    return;
                }
                HandleBattleSettlement(account, p, responses, result, levelConfig);
                break;
            case 2:
                // nState=2: 章节星级奖励请求（参数 Chapter/Difficult/Pos）
                // 客户端进入章节时也可能发 nState=2（Index=0），需要同步章节星级任务值
                var player2 = _repo.Get(account);
                _logger.Info($"lua.chapterMsg.star_award account={account} chapter={chapter} index={index} diff={difficult} playerLevel={player2?.Level}");
                // 同步章节星级任务值（确保客户端能正确显示章节星级奖励状态）
                if (player2 != null && chapter > 0)
                {
                    _repo.Modify(account, p =>
                    {
                        foreach (int diff in new[] { 1, 2 })
                        {
                            var taskId = ChapterStarAwardData.MakeTaskId((int)chapter, diff);
                            var totalStars = ChapterStarAwardData.ChapterTotalStars(p.Levels, (int)chapter, diff);
                            var existingVal = p.TaskValues.TryGetValue(taskId.ToString(), out var ev) ? (int)ev : 0;
                            var claimedMask = ChapterStarAwardData.ClaimedMask(existingVal);
                            p.TaskValues[taskId.ToString()] = ChapterStarAwardData.MakeTaskValue(totalStars, claimedMask);
                        }
                    });
                }
                responses.Add(MakeS2CCall("ChapterMsg", new { nError = 0, nState = 2, }));
                break;
            default:
                responses.Add(MakeS2CCall("ChapterMsg", new { nError = 0, nState }));
                break;
        }
    }

    private void HandleBattleSettlement(string account, JsonElement p, List<byte[]> responses, LuaCallResult result, ChapterConfig.LevelInfo? levelConfig = null)
    {
        try
        {
            var chapter = p.TryGetProperty("Chapter", out var ch) ? ch.GetInt64() : 0;
            var index = p.TryGetProperty("Index", out var idx) ? idx.GetInt64() : 0;
            var difficult = p.TryGetProperty("Difficult", out var diff) ? diff.GetInt64() : 0;
            var nStar = p.TryGetProperty("nStar", out var star) ? star.GetInt64() : 3;

            _logger.Info($"lua.battle_settlement account={account} chapter={chapter} index={index} diff={difficult} star={nStar}");

            long completedStar = 0;
            _repo.Modify(account, player =>
            {
                var levelId = (chapter << 16) | (index << 8) | difficult;
                const long starMask = 7;
                var existingLevel = player.Levels.FirstOrDefault(l => l.Id == levelId);
                if (existingLevel != null)
                {
                    var passCount = Math.Min((existingLevel.Star >> 3) + 1, 0x0fffffff);
                    existingLevel.Star = (passCount << 3) | (existingLevel.Star & 0b111) | starMask;
                    completedStar = existingLevel.Star;
                }
                else
                {
                    completedStar = (1 << 3) | starMask;
                    player.Levels.Add(new LevelState { Id = levelId, Star = completedStar });
                }

                // 使用正确的章节星级任务ID格式：(14<<16)|(difficulty|(chapter<<8))
                var chapterStarTaskId = ChapterStarAwardData.MakeTaskId((int)chapter, (int)difficult);
                var chapterTotalStars = ChapterStarAwardData.ChapterTotalStars(player.Levels, (int)chapter, (int)difficult);
                var existingTaskVal = player.TaskValues.TryGetValue(chapterStarTaskId.ToString(), out var etv) ? (int)etv : 0;
                var claimedMask = ChapterStarAwardData.ClaimedMask(existingTaskVal);
                player.TaskValues[chapterStarTaskId.ToString()] = ChapterStarAwardData.MakeTaskValue(chapterTotalStars, claimedMask);
                _logger.Info($"chapter.star_sync account={account} chapter={chapter} diff={difficult} totalStars={chapterTotalStars} taskId={chapterStarTaskId}");

                UpdateGuideMissionProgress(player, chapter, index, difficult);
                // 新增：使用完整引导任务配置同步进度（保留原有逻辑）
                GuideMissionData.SyncByLevel(player.TaskValues, (int)chapter, (int)index, (int)difficult);

                var vigour = player.Money.FirstOrDefault(m => m.Id == 1);
                if (vigour != null && vigour.Count > 0)
                {
                    vigour.Count = Math.Max(0, vigour.Count - 3);
                    result.UpdatedMoney.Add(vigour);
                }
                var gold = player.Money.FirstOrDefault(m => m.Id == 2);
                if (gold != null)
                {
                    gold.Count += 500;
                    result.UpdatedMoney.Add(gold);
                }
                var (newLevel, newExp, levelsGained) = PlayerLevelData.AddExperience((int)player.Level, player.Exp, 100);
                if (levelsGained > 0 || newExp != player.Exp)
                {
                    player.Level = newLevel;
                    player.Exp = newExp;
                    result.ExperienceChanged = true;
                }
                foreach (var girl in player.Girls)
                {
                    girl.Exp += 50;
                    result.UpdatedGirls.Add(girl);
                }

                var rng = new Random();
                var dropCount = rng.Next(1, 4);
                for (int i = 0; i < dropCount; i++)
                {
                    var dropType = rng.Next(100);
                    if (dropType < 30)
                    {
                        var diamond = player.Money.FirstOrDefault(m => m.Id == 3);
                        if (diamond != null) { diamond.Count += rng.Next(10, 50); result.UpdatedMoney.Add(diamond); }
                    }
                    else if (dropType < 60)
                    {
                        var matGuid = player.NextItemGuid++;
                        player.Inventory.Add(new InventoryEntry
                        {
                            Guid = matGuid, Genre = 7, Detail = 3, Particular = 1,
                            TemplateLevel = rng.Next(1, 5), Count = rng.Next(1, 5),
                            CreateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                            EnhanceLevel = 1, EnhanceExp = 0, BreakLevel = 0, LockOn = 0,
                        });
                        result.UpdatedItems.Add(player.Inventory.Last());
                    }
                    else if (dropType < 80)
                    {
                        var matGuid = player.NextItemGuid++;
                        player.Inventory.Add(new InventoryEntry
                        {
                            Guid = matGuid, Genre = 15, Detail = 1, Particular = 1,
                            TemplateLevel = 1, Count = rng.Next(1, 3),
                            CreateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                            EnhanceLevel = 1, EnhanceExp = 0, BreakLevel = 0, LockOn = 0,
                        });
                        result.UpdatedItems.Add(player.Inventory.Last());
                    }
                }

                result.NeedsPlayerSync = true;
            });

            responses.Add(MakeS2CCall("ChapterMsg", new
            {
                nError = 0, nState = 1, nStar = completedStar, tbAwards = Array.Empty<object>(),
                tbExp = new { MasterExp = 100, CardExp = 50, },
            }));
        }
        catch (Exception ex)
        {
            _logger.Error($"lua.battle_error {ex.Message}");
            responses.Add(MakeS2CCall("ChapterMsg", new { nError = 1, nState = 1 }));
        }
    }

    // ---- 签到 ----

    private void HandleSignUp(string account, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        var nType = p.TryGetProperty("nType", out var t) ? t.GetInt64() : 1;
        _logger.Info($"lua.signUp_blocked account={account} type={nType}");

        _repo.Modify(account, player =>
        {
            var todayKey = "sign_" + DateTime.UtcNow.ToString("yyyy-MM-dd");
            player.TaskValues[todayKey] = 1;
        });

        responses.Add(MakeS2CCall("NormalActivityMsg", new
        {
            nType = 3, nSubType = nType, bSuccess = true, tbAward = Array.Empty<object>(), isRefreshSign = false,
        }));
    }

    // ---- 任务领奖 ----

    private void HandleMissionAward(string account, string method, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        var id = p.TryGetProperty("nId", out var idEl) ? idEl.GetInt64() : 0;
        var nType = p.TryGetProperty("nType", out var t) ? t.GetInt64() : 0;
        _logger.Info($"lua.mission_award account={account} method={method} id={id} type={nType}");

        _repo.Modify(account, player =>
        {
            if (method == "MissionGetAward" && id == 0 && nType == 1)
            {
                for (int missionId = 101; missionId <= 110; missionId++)
                {
                    long taskId = (5L << 16) | (uint)missionId;
                    var key = taskId.ToString();
                    if (player.TaskValues.TryGetValue(key, out var val) && (val & 1) == 0)
                    {
                        player.TaskValues[key] = val | 1;
                    }
                }
                var gold = player.Money.FirstOrDefault(m => m.Id == 2);
                if (gold != null) gold.Count += 10000;
            }
            else if (id > 0)
            {
                long taskId = (5L << 16) | (uint)id;
                var key = taskId.ToString();
                if (player.TaskValues.TryGetValue(key, out var val) && (val & 1) == 0)
                {
                    player.TaskValues[key] = val | 1;
                    var gold = player.Money.FirstOrDefault(m => m.Id == 2);
                    if (gold != null) gold.Count += 1000;
                }
            }
        });

        responses.Add(MakeS2CCall("MissionMgrMsg", new { nError = 0, nMission = method == "MissionActiveAward" ? 2 : 1, }));
    }

    // ---- 引导任务 ----

    private static readonly Dictionary<long, (long chapter, long index, long diff, long target)> GuideLevelMissions = new()
    {
        { 40001, (1, 1, 1, 1) }, { 40002, (1, 2, 1, 1) }, { 40003, (1, 3, 1, 1) },
        { 40004, (1, 6, 1, 1) }, { 40005, (2, 6, 1, 1) }, { 40006, (3, 6, 1, 1) },
        { 40025, (1, 1, 2, 1) },
    };

    private static void UpdateGuideMissionProgress(PlayerState player, long chapter, long index, long diff)
    {
        foreach (var kv in GuideLevelMissions)
        {
            var (c, i, d, target) = kv.Value;
            if (c == chapter && i == index && d == diff)
            {
                long taskId = (5L << 16) | (uint)kv.Key;
                var key = taskId.ToString();
                var current = player.TaskValues.TryGetValue(key, out var v) ? v : 0;
                var claimed = (current & 1) == 1;
                player.TaskValues[key] = target * 2 + (claimed ? 1 : 0);
            }
        }
        if (player.Formations.Any(f => f.FightCards.Any(c => c.WeaponGuid > 0)))
        {
            long taskId = (5L << 16) | 40027;
            var key = taskId.ToString();
            if (!player.TaskValues.ContainsKey(key))
                player.TaskValues[key] = 2;
        }
        long powerTaskId = (5L << 16) | 40022;
        var powerKey = powerTaskId.ToString();
        var powerCurrent = player.TaskValues.TryGetValue(powerKey, out var pv) ? pv : 0;
        var powerClaimed = (powerCurrent & 1) == 1;
        // 战力固定设为 999999，确保引导任务达成
        player.TaskValues[powerKey] = 999999 * 2 + (powerClaimed ? 1 : 0);
    }

    private void HandleGuideAward(string account, string method, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        var id = p.TryGetProperty("nId", out var idEl) ? idEl.GetInt64() : 0;
        _logger.Info($"lua.guide_award account={account} method={method} id={id}");

        _repo.Modify(account, player =>
        {
            if (id > 0)
            {
                long taskId = (5L << 16) | (uint)id;
                var key = taskId.ToString();
                if (player.TaskValues.TryGetValue(key, out var val) && (val & 1) == 0)
                {
                    player.TaskValues[key] = val | 1;
                    var gold = player.Money.FirstOrDefault(m => m.Id == 2);
                    if (gold != null) gold.Count += 2000;
                }
            }
        });

        responses.Add(MakeS2CCall("MissionMgrMsg", new { nError = 0, nMission = method == "GuideProgressGetAward" ? 4 : 3, }));
    }

    // ---- 活动领奖 ----

    private void HandleNormalActivityAward(string account, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        try
        {
            var activityId = p.TryGetProperty("nActivityId", out var a) ? a.GetInt64() : 0;
            var id = p.TryGetProperty("nId", out var i) ? i.GetInt64() : 0;
            _logger.Info($"lua.activity_award account={account} activityId={activityId} id={id}");

            _repo.Modify(account, player =>
            {
                var gold = player.Money.FirstOrDefault(m => m.Id == 2);
                if (gold != null) { gold.Count += 1000; result.UpdatedMoney.Add(gold); }
                var diamond = player.Money.FirstOrDefault(m => m.Id == 3);
                if (diamond != null) { diamond.Count += 50; result.UpdatedMoney.Add(diamond); }
            });

            responses.Add(MakeS2CCall("MissionMgrMsg", new { nError = 0, nMission = 0 }));
        }
        catch (Exception ex)
        {
            _logger.Error($"lua.activity_award_error {ex.Message}");
            responses.Add(MakeS2CCall("MissionMgrMsg", new { nError = 1, nMission = 0 }));
        }
    }

    // ---- 物品锁定 ----

    private void HandleLockItem(string account, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        try
        {
            var guid = p.TryGetProperty("nGuid", out var g) ? g.GetInt64() : 0;
            var lockOn = p.TryGetProperty("nLockOn", out var l) ? l.GetInt64() : 0;

            _repo.Modify(account, player =>
            {
                var item = player.Inventory.FirstOrDefault(it => it.Guid == guid);
                if (item != null) item.LockOn = lockOn;
            });
            _logger.Info($"lua.lock_item account={account} guid={guid} lockOn={lockOn}");
        }
        catch (Exception ex)
        {
            _logger.Error($"lua.lock_item_error {ex.Message}");
        }
    }

    // ---- 短信 PhoneMsg ----

    private void HandlePhoneMsg(string account, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        try
        {
            var nCmd = p.TryGetProperty("nCmd", out var cmd) ? cmd.GetInt64() : 0;
            _logger.Info($"lua.phoneMsg account={account} nCmd={nCmd} params={p.GetRawText()}");

            var resp = new Dictionary<string, object> { ["nCmd"] = nCmd };

            if (nCmd == 8)
            {
                resp["tbList"] = Array.Empty<object>();
            }
            else if (nCmd == 3)
            {
                var nMsgId = p.TryGetProperty("nMsgId", out var m) ? m.GetInt64() : 0;
                var nSelectId = p.TryGetProperty("nSelectId", out var s) ? s.GetInt64() : 0;
                resp["nNpcId"] = 1;
                resp["nMsgId"] = nMsgId;
                resp["nSelectId"] = nSelectId;
            }
            else if (nCmd == 10 || nCmd == 11)
            {
                var nMsgId = p.TryGetProperty("nMsgId", out var m) ? m.GetInt64() : 0;
                resp["nNpcId"] = 1;
                resp["nMsgId"] = nMsgId;
            }

            responses.Add(MakeS2CCall("ServerPhoneMsg", resp));
        }
        catch (Exception ex)
        {
            _logger.Error($"lua.phoneMsg_error {ex.Message}");
            responses.Add(MakeS2CCall("ServerPhoneMsg", new { nCmd = 0 }));
        }
    }

    // ---- 抽卡 Lottery ----

    private static readonly int[] GachaCharacterIds = Enumerable.Range(1, 50).ToArray();
    private static readonly Random _rng = new();

    private void HandleLottery(string account, string method, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        try
        {
            var isFirstGacha = method == "GetFirstGacha";
            var ten = isFirstGacha || (p.TryGetProperty("bTen", out var bt) && bt.GetBoolean());
            var count = ten ? 10 : 1;
            var cost = ten ? 1500 : 150;

            var awards = new List<object>();

            _repo.Modify(account, player =>
            {
                var diamond = player.Money.FirstOrDefault(m => m.Id == 3);
                if (diamond == null || diamond.Count < cost)
                {
                    responses.Add(MakeS2CCall("Lottery", new { err = "error.gacha.cash" }));
                    return;
                }
                diamond.Count -= cost;
                result.UpdatedMoney.Add(diamond);

                var nextGuid = player.Inventory.Count > 0 ? player.Inventory.Max(i => i.Guid) + 1 : 10001;
                for (int i = 0; i < count; i++)
                {
                    var charId = GachaCharacterIds[_rng.Next(GachaCharacterIds.Length)];
                    var rarity = _rng.Next(100) < 3 ? 3 : (_rng.Next(100) < 15 ? 2 : 1);
                    var tplLevel = rarity == 3 ? 3 : (rarity == 2 ? 2 : 1);
                    var guid = nextGuid++;

                    var newItem = new InventoryEntry
                    {
                        Guid = guid, Genre = 1, Detail = charId, Particular = 1,
                        TemplateLevel = tplLevel, Count = 1,
                        CreateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        EnhanceLevel = 1, EnhanceExp = 0, BreakLevel = 0, LockOn = 0,
                    };
                    player.Inventory.Add(newItem);
                    result.UpdatedItems.Add(newItem);

                    var hasCard = player.Inventory.Any(it => it.Genre == 1 && it.Detail == charId && it.Guid != guid);
                    if (!hasCard)
                    {
                        if (!player.Girls.Any(g => g.GirlId == charId))
                        {
                            player.Girls.Add(new GirlState
                            {
                                GirlId = charId, Level = 1, Exp = 0, ModelId = 1,
                                MoodValue = 100, Vigor = 100, Flag = 0, BreakLevel = 0,
                            });
                            result.UpdatedGirls.Add(player.Girls.Last());
                        }
                    }

                    awards.Add(new
                    {
                        tbGDPL = new[] { 1, charId, 1, tplLevel }, nId = charId,
                        nTimes = i + 1, nTotalTimes = i + 1, isUp = false, bFirstGet = !hasCard, bHasCard = hasCard,
                    });
                }

                long MakeGachaTaskId(long taskId) => (20L << 16) | taskId;
                var totalTaskId = MakeGachaTaskId(10005).ToString();
                var pityTaskId = MakeGachaTaskId(2).ToString();
                player.TaskValues[totalTaskId] = (player.TaskValues.TryGetValue(totalTaskId, out var t) ? t : 0) + count;
                player.TaskValues[pityTaskId] = 0;
            });

            if (responses.Count > 0) return; // 已经在 Modify 里添加了错误响应

            _logger.Info($"lua.lottery account={account} ten={ten} count={count} cost={cost} awards={awards.Count}");

            // bGetCard 总是 true，不返回 getItem（卡池无固定奖励配置）
            responses.Add(MakeS2CCall("Lottery", new { bTen = ten, bGetCard = true, tbAwards = awards, }));
        }
        catch (Exception ex)
        {
            _logger.Error($"lua.lottery_error {ex.Message}");
            responses.Add(MakeS2CCall("Lottery", new { err = "error.gacha.cash" }));
        }
    }

    // ---- 武器逻辑 ----

    private void HandleWeaponLogic(string account, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        try
        {
            var nCmd = p.TryGetProperty("nCmd", out var cmd) ? cmd.GetInt64() : 0;
            _logger.Info($"lua.weaponLogic account={account} nCmd={nCmd}");

            if (nCmd == 2)
            {
                HandleWeaponDecompose(account, p, responses, result, nCmd);
                return;
            }
            if (nCmd == 1)
            {
                HandleWeaponEnhance(account, p, responses, result, nCmd);
                return;
            }
            responses.Add(MakeS2CCall("WeaponLogicMsg", new { nError = 0, nCmd }));
        }
        catch (Exception ex)
        {
            _logger.Error($"lua.weapon_error {ex.Message}");
            responses.Add(MakeS2CCall("WeaponLogicMsg", new { nError = 1, nCmd = 0 }));
        }
    }

    private void HandleWeaponDecompose(string account, JsonElement p, List<byte[]> responses, LuaCallResult result, long nCmd)
    {
        var guids = ParseGuidList(p);
        if (guids.Count == 0)
        {
            responses.Add(MakeS2CCall("WeaponLogicMsg", new { nError = 1, nCmd }));
            return;
        }

        long goldReward = 0;
        var toRemove = new List<long>();
        var error = 0;

        _repo.Modify(account, player =>
        {
            var equippedGuids = new HashSet<long>(player.Formations.SelectMany(f => f.FightCards.Select(c => c.WeaponGuid)));
            foreach (var guid in guids)
            {
                var weapon = player.Inventory.FirstOrDefault(i => i.Guid == guid && i.Genre == 2);
                if (weapon == null) continue;
                if (weapon.LockOn == 1) { error = 20015; return; }
                if (equippedGuids.Contains(guid)) { error = 20016; return; }
                goldReward += weapon.TemplateLevel * 1000 + weapon.EnhanceLevel * 100;
                toRemove.Add(guid);
            }
            player.Inventory.RemoveAll(i => toRemove.Contains(i.Guid));
            var gold = player.Money.FirstOrDefault(m => m.Id == 2);
            if (gold != null) { gold.Count += goldReward; result.UpdatedMoney.Add(gold); }
        });

        if (error != 0)
        {
            responses.Add(MakeS2CCall("WeaponLogicMsg", new { nError = error, nCmd }));
            return;
        }

        _logger.Info($"lua.weapon_decompose account={account} count={toRemove.Count} gold={goldReward}");
        responses.Add(MakeS2CCall("WeaponLogicMsg", new { nError = 0, nCmd, tbParam = new object[] { goldReward, Array.Empty<object>() } }));
    }

    private void HandleWeaponEnhance(string account, JsonElement p, List<byte[]> responses, LuaCallResult result, long nCmd)
    {
        long weaponGuid = 0;
        var materials = ParseMaterialList(p);

        if (p.TryGetProperty("nGuid", out var wg)) weaponGuid = wg.GetInt64();
        if (weaponGuid == 0)
        {
            responses.Add(MakeS2CCall("WeaponLogicMsg", new { nError = 1, nCmd }));
            return;
        }

        long totalExp = 0, totalCost = 0;
        var error = 0;

        _repo.Modify(account, player =>
        {
            var weapon = player.Inventory.FirstOrDefault(i => i.Guid == weaponGuid && i.Genre == 2);
            if (weapon == null) { error = 1; return; }

            var consumed = new List<long>();
            foreach (var (matGuid, count) in materials)
            {
                var mat = player.Inventory.FirstOrDefault(i => i.Guid == matGuid);
                if (mat == null || mat.Count < count) continue;
                totalExp += 500 * count;
                totalCost += 100 * count;
                mat.Count -= count;
                consumed.Add(matGuid);
            }

            var gold = player.Money.FirstOrDefault(m => m.Id == 2);
            if (gold == null || gold.Count < totalCost) { error = 1; return; }
            gold.Count -= totalCost;
            result.UpdatedMoney.Add(gold);

            weapon.EnhanceExp += totalExp;
            while (weapon.EnhanceExp >= 1000 && weapon.EnhanceLevel < 100)
            {
                weapon.EnhanceExp -= 1000;
                weapon.EnhanceLevel += 1;
            }
            result.UpdatedItems.Add(weapon);
            foreach (var cg in consumed)
            {
                var item = player.Inventory.FirstOrDefault(i => i.Guid == cg);
                if (item != null) result.UpdatedItems.Add(item);
            }
        });

        if (error != 0)
        {
            responses.Add(MakeS2CCall("WeaponLogicMsg", new { nError = 1, nCmd }));
            return;
        }

        _logger.Info($"lua.weapon_enhance account={account} guid={weaponGuid} exp={totalExp}");
        responses.Add(MakeS2CCall("WeaponLogicMsg", new { nError = 0, nCmd }));
    }

    // ---- 角色卡分解 ----

    private void HandleCardDecompose(string account, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        try
        {
            var guids = ParseGuidList(p);
            long goldReward = 0;
            var toRemove = new List<long>();

            _repo.Modify(account, player =>
            {
                foreach (var guid in guids)
                {
                    var card = player.Inventory.FirstOrDefault(i => i.Guid == guid && i.Genre == 1);
                    if (card == null || card.LockOn == 1) continue;
                    goldReward += card.TemplateLevel * 500 + card.EnhanceLevel * 50;
                    toRemove.Add(guid);
                }
                player.Inventory.RemoveAll(i => toRemove.Contains(i.Guid));
                var gold = player.Money.FirstOrDefault(m => m.Id == 2);
                if (gold != null) { gold.Count += goldReward; result.UpdatedMoney.Add(gold); }
            });

            _logger.Info($"lua.card_decompose account={account} count={toRemove.Count} gold={goldReward}");
            responses.Add(MakeS2CCall("LuaCall", new { sCmd = 1, tbParam = new { nError = 0, tbAward = new object[] { new[] { 15, 1, 1, 1, goldReward } } } }));
        }
        catch (Exception ex)
        {
            _logger.Error($"lua.card_decompose_error {ex.Message}");
            responses.Add(MakeS2CCall("LuaCall", new { sCmd = 1, bSuccess = false }));
        }
    }

    // ---- 角色卡强化 ----

    private void HandleCardEnhance(string account, JsonElement p, List<byte[]> responses, LuaCallResult result)
    {
        try
        {
            long cardGuid = 0;
            var materials = ParseMaterialList(p);
            if (p.TryGetProperty("nGuid", out var cg)) cardGuid = cg.GetInt64();
            if (cardGuid == 0) { responses.Add(MakeS2CCall("LuaCall", new { sCmd = 5, bSuccess = false })); return; }

            long totalExp = 0, totalCost = 0;
            var error = false;

            _repo.Modify(account, player =>
            {
                var card = player.Inventory.FirstOrDefault(i => i.Guid == cardGuid && i.Genre == 1);
                if (card == null) { error = true; return; }

                foreach (var (matGuid, count) in materials)
                {
                    var mat = player.Inventory.FirstOrDefault(i => i.Guid == matGuid);
                    if (mat == null || mat.Count < count) continue;
                    totalExp += 300 * count;
                    totalCost += 50 * count;
                    mat.Count -= count;
                }
                var gold = player.Money.FirstOrDefault(m => m.Id == 2);
                if (gold == null || gold.Count < totalCost) { error = true; return; }
                gold.Count -= totalCost;
                result.UpdatedMoney.Add(gold);

                card.EnhanceExp += totalExp;
                while (card.EnhanceExp >= 1000 && card.EnhanceLevel < 100)
                {
                    card.EnhanceExp -= 1000;
                    card.EnhanceLevel += 1;
                }
                result.UpdatedItems.Add(card);
            });

            if (error) { responses.Add(MakeS2CCall("LuaCall", new { sCmd = 5, bSuccess = false })); return; }

            _logger.Info($"lua.card_enhance account={account} guid={cardGuid} exp={totalExp}");
            responses.Add(MakeS2CCall("LuaCall", new { sCmd = 5, tbParam = new { nError = 0 } }));
        }
        catch (Exception ex)
        {
            _logger.Error($"lua.card_enhance_error {ex.Message}");
            responses.Add(MakeS2CCall("LuaCall", new { sCmd = 5, bSuccess = false }));
        }
    }

    // ---- 解析辅助 ----

    private static List<long> ParseGuidList(JsonElement p)
    {
        var guids = new List<long>();
        if (p.TryGetProperty("tbParam", out var tp) && tp.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in tp.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.Number) guids.Add(el.GetInt64());
                else if (el.TryGetProperty("nGuid", out var g)) guids.Add(g.GetInt64());
            }
        }
        if (guids.Count == 0 && p.TryGetProperty("nGuid", out var sg))
            guids.Add(sg.GetInt64());
        return guids;
    }

    private static List<(long guid, long count)> ParseMaterialList(JsonElement p)
    {
        var materials = new List<(long, long)>();
        if (p.TryGetProperty("tbParam", out var mp) && mp.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in mp.EnumerateArray())
            {
                if (el.TryGetProperty("guid", out var mg) && el.TryGetProperty("count", out var mc))
                    materials.Add((mg.GetInt64(), mc.GetInt64()));
                else if (el.TryGetProperty("nGuid", out var mg2) && el.TryGetProperty("nCount", out var mc2))
                    materials.Add((mg2.GetInt64(), mc2.GetInt64()));
            }
        }
        return materials;
    }

    // ---- 女孩相关 taskId 计算 ----

    private const long GIRL_STATE_TASK_GROUP = 3;
    private const long GIRL_SUIT_TASK_GROUP = 4;
    private const long GIRL_TASK_STRIDE = 2000;
    private const long GIRL_FIGHT_MODEL_OFFSET = 9;

    private static long MakeGirlTaskId(long taskGroup, long girlId, long offset)
    {
        return (taskGroup << 16) | ((girlId - 1) * GIRL_TASK_STRIDE + offset);
    }

    private static long FixGirlModelTaskOffset(long modelId)
    {
        if (modelId <= 0) return 1;
        if (modelId < 2000) return modelId;
        if (modelId >= 8001 && modelId <= 8003) return modelId - 6200;
        if (modelId >= 3000)
        {
            var suffix = modelId % 100;
            if (suffix <= 29) return suffix + (modelId / 1000) * 30 + 1500;
        }
        return 1;
    }

    // ---- GM命令执行 ----

    private string ExecuteGmCommand(string account, string command)
    {
        try
        {
            var parts = command.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "空命令";
            var cmd = parts[0].ToLower();

            switch (cmd)
            {
                case "level":
                    if (parts.Length < 2) return "用法: level <n>";
                    _repo.Modify(account, p => { p.Level = Math.Max(1, int.Parse(parts[1])); });
                    return $"等级已设置";
                case "exp":
                    if (parts.Length < 2) return "用法: exp <n>";
                    _repo.Modify(account, p => { p.Exp = long.Parse(parts[1]); });
                    return $"经验已设置";
                case "vigor":
                case "体力":
                    if (parts.Length < 2) return "用法: vigor <n>";
                    _repo.Modify(account, p =>
                    {
                        var vig = p.Money.FirstOrDefault(m => m.Id == 1);
                        if (vig != null) vig.Count = long.Parse(parts[1]);
                    });
                    return "体力已设置";
                case "gold":
                case "金币":
                    if (parts.Length < 2) return "用法: gold <n>";
                    _repo.Modify(account, p =>
                    {
                        var gold = p.Money.FirstOrDefault(m => m.Id == 2);
                        if (gold != null) gold.Count = long.Parse(parts[1]);
                    });
                    return "金币已设置";
                case "diamond":
                case "青辉石":
                    if (parts.Length < 2) return "用法: diamond <n>";
                    _repo.Modify(account, p =>
                    {
                        var dia = p.Money.FirstOrDefault(m => m.Id == 3);
                        if (dia != null) dia.Count = long.Parse(parts[1]);
                    });
                    return "青辉石已设置";
                case "unlockall":
                case "解锁全部":
                    _repo.Modify(account, p =>
                    {
                        // 解锁全部主线关卡（Chapter 1-16 + Chapter 100 誓约关卡）
                        foreach (var lv in ChapterConfig.AllLevels)
                        {
                            var levelId = (long)(lv.Chapter << 16) | (lv.Index << 8) | lv.Difficulty;
                            if (!p.Levels.Any(l => l.Id == levelId))
                            {
                                p.Levels.Add(new LevelState { Id = levelId, Star = (1 << 3) | 7 });
                            }
                        }
                    });
                    return "已解锁全部关卡";
                case "maxcards":
                case "全角色":
                    _repo.Modify(account, p =>
                    {
                        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        var ng = p.Inventory.Count > 0 ? p.Inventory.Max(i => i.Guid) + 1 : 20001;
                        var allCards = CharacterCardData.GetAllCards();
                        // 添加所有女孩（去重）
                        var girlIds = allCards.Select(c => c.girlId).Distinct().ToList();
                        foreach (var gid in girlIds)
                        {
                            if (!p.Girls.Any(g => g.GirlId == gid))
                            {
                                p.Girls.Add(new GirlState
                                {
                                    GirlId = gid, Level = 80, Exp = 0, ModelId = 1,
                                    MoodValue = 100, Vigor = 100, BreakLevel = 7,
                                });
                            }
                        }
                        // 添加所有角色卡（满级满突破）
                        // 按(女孩ID, 服装ID)分组，保留最高星级（客户端Particular=服装ID，同一服装只显示最高星）
                        var bestCards = allCards
                            .GroupBy(c => (c.girlId, c.costumeId))
                            .Select(g => g.OrderByDescending(c => c.star).First())
                            .ToList();
                        foreach (var (girlId, costumeId, star) in bestCards)
                        {
                            if (!p.Inventory.Any(i => i.Genre == 1 && i.Detail == girlId && i.Particular == costumeId))
                            {
                                p.Inventory.Add(new InventoryEntry
                                {
                                    Guid = ng++, Genre = 1, Detail = girlId, Particular = costumeId,
                                    TemplateLevel = star, Count = 1, CreateTime = now,
                                    EnhanceLevel = 100, EnhanceExp = 0, BreakLevel = 5, LockOn = 0,
                                });
                            }
                        }
                        p.NextItemGuid = ng;
                    });
                    return $"已添加所有角色（{CharacterCardData.GetAllCards().Count}张角色卡，女孩+角色卡，满级满突破）";
                case "maxlevel":
                case "全角色满级":
                    _repo.Modify(account, p =>
                    {
                        // 所有女孩满级满突破
                        foreach (var g in p.Girls) { g.Level = 80; g.Exp = 0; g.BreakLevel = 7; }
                        // 所有角色卡满级满突破
                        foreach (var item in p.Inventory.Where(i => i.Genre == 1))
                        {
                            item.EnhanceLevel = 100;
                            item.EnhanceExp = 0;
                            item.BreakLevel = 5;
                        }
                    });
                    return "已将所有角色（女孩+角色卡）升至满级满突破";
                case "addcard":
                    if (parts.Length < 2) return "用法: addcard <id>";
                    _repo.Modify(account, p =>
                    {
                        var ac = p.Inventory.Count > 0 ? p.Inventory.Max(i => i.Guid) + 1 : 20001;
                        p.Inventory.Add(new InventoryEntry { Guid = ac, Genre = 1, Detail = int.Parse(parts[1]), Particular = 1, TemplateLevel = 3, Count = 1, CreateTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), EnhanceLevel = 1 });
                    });
                    return $"已添加角色卡 {parts[1]}";
                case "addgirl":
                    if (parts.Length < 2) return "用法: addgirl <id>";
                    _repo.Modify(account, p =>
                    {
                        var gid = int.Parse(parts[1]);
                        if (!p.Girls.Any(g => g.GirlId == gid))
                            p.Girls.Add(new GirlState { GirlId = gid, Level = 1, Exp = 0, ModelId = 1, MoodValue = 100, Vigor = 100 });
                    });
                    return $"已添加女孩 {parts[1]}";
                case "maxmoney":
                case "全货币":
                    _repo.Modify(account, p =>
                    {
                        var moneyIds = new long[] { 1, 2, 3, 4, 5, 6, 7, 10, 12, 16, 18, 19, 20 };
                        foreach (var mid in moneyIds)
                        {
                            var m = p.Money.FirstOrDefault(x => x.Id == mid);
                            if (m == null) p.Money.Add(new MoneyEntry { Id = mid, Count = 9999999 });
                            else m.Count = 9999999;
                        }
                    });
                    return "所有货币已拉满";
                case "allitems":
                case "全物品":
                    _repo.Modify(account, p =>
                    {
                        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        var ng = p.Inventory.Count > 0 ? p.Inventory.Max(i => i.Guid) + 1 : 20001;
                        void AddItem(int genre, int detail, int particular, int count, int enhance = 0, int breakLv = 0, int tplLevel = 1)
                        {
                            if (!p.Inventory.Any(i => i.Genre == genre && i.Detail == detail && i.Particular == particular))
                                p.Inventory.Add(new InventoryEntry { Guid = ng++, Genre = genre, Detail = detail, Particular = particular, TemplateLevel = tplLevel, Count = count, CreateTime = now, EnhanceLevel = enhance, EnhanceExp = 0, BreakLevel = breakLv, LockOn = 0 });
                        }
                        // 全部武器（满级满突破），从 weapons.txt 读取所有138个武器
                        foreach (var w in WeaponSkillData.GetAllWeapons())
                        {
                            if (!p.Inventory.Any(i => i.Genre == w.Genre && i.Detail == w.Detail && i.Particular == w.Particular))
                            {
                                p.Inventory.Add(new InventoryEntry { Guid = ng++, Genre = w.Genre, Detail = w.Detail, Particular = w.Particular, TemplateLevel = w.Rarity, Count = 1, CreateTime = now, EnhanceLevel = 100, EnhanceExp = 999999, BreakLevel = 4, LockOn = 0 });
                            }
                        }
                        for (int d = 1; d <= 500; d++) AddItem(13, d, 1, 99); // 家具
                        for (int d = 1; d <= 200; d++) AddItem(14, d, 1, 99); // 装饰
                        for (int d = 1; d <= 100; d++) AddItem(15, d, 1, 999); // 消耗品
                        for (int d = 10000; d <= 10500; d++) AddItem(10, d, 1, 1); // 头像框
                        for (int d = 11000; d <= 11200; d++) AddItem(11, d, 1, 1); // 展示柜
                        for (int d = 12000; d <= 12200; d++) AddItem(12, d, 1, 1); // 聊天泡泡
                        // 满级模块：detail 1-100, particular 1-7
                        for (int d = 1; d <= 100; d++)
                            for (int tpl = 1; tpl <= 7; tpl++)
                                AddItem(3, d, tpl, 999, enhance: 100, breakLv: 4, tplLevel: tpl);
                        p.NextItemGuid = ng;
                    });
                    return "已添加所有武器/家具/装饰/消耗品/头像框/展示柜/聊天泡泡/满级模块";
                case "help":
                    return "命令: level/exp/vigor/gold/diamond <n>, unlockall, maxcards, maxlevel, addcard/addgirl <id>";
                default:
                    return $"未知命令: {cmd}，输入 !help 查看帮助";
            }
        }
        catch (Exception ex)
        {
            return $"执行失败: {ex.Message}";
        }
    }

    // ---- 工具 ----

    public static byte[] MakeS2CCall(string method, object parameters)
    {
        var json = JsonSerializer.Serialize(parameters);
        return Concat(FieldBytes(1, method), FieldBytes(2, json));
    }

    public static (string method, JsonElement parameters)? ParseCall(byte[] payload)
    {
        try
        {
            var fields = ProtobufReader.Decode(payload);
            var method = ProtobufReader.FirstString(fields, 1);
            var jsonStr = ProtobufReader.FirstString(fields, 2);
            if (string.IsNullOrEmpty(method) || string.IsNullOrEmpty(jsonStr))
                return null;
            using var doc = JsonDocument.Parse(jsonStr);
            return (method, doc.RootElement.Clone());
        }
        catch
        {
            return null;
        }
    }
}
