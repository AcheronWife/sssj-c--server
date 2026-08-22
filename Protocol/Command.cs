namespace Gcg2OfflineServer.Protocol;

/// <summary>
/// 协议号定义。
/// </summary>
public static class Command
{
    public const ushort LoginReq            = 1001;
    public const ushort LoginRsp            = 1002;
    public const ushort PlayerNtf           = 1005;
    public const ushort RenameReq           = 1006;
    public const ushort RenameRsp           = 1007;
    public const ushort KeepAliveReq        = 1008;
    public const ushort KeepAliveRsp        = 1009;
    public const ushort C2sCallReq          = 1022;
    public const ushort C2sCallRsp          = 1023;
    public const ushort NtfS2cCall          = 1024;
    public const ushort TaskValueReq        = 1025;
    public const ushort TaskValueRsp        = 1026;
    public const ushort TaskChangeReq       = 1027;
    public const ushort TaskChangeRsp       = 1028;
    public const ushort PlayerUpdateNtf     = 1029;
    public const ushort GirlUpdateNtf       = 1030;
    public const ushort ItemUpdateNtf       = 1031;
    public const ushort MoneyUpdateNtf      = 1032;
    public const ushort FormationUpdateNtf  = 1033;
    public const ushort ChapterUpdateNtf    = 1034;
    public const ushort PhoneMsgNtf         = 1035;
    public const ushort Live2dEnableLevelNtf = 1036;
    public const ushort Live2dHxStateNtf    = 1037;
    public const ushort GetHouseinfoReq     = 1048;
    public const ushort GetHouseinfoRsp     = 1049;
    public const ushort VerifyReq           = 1102;
    public const ushort VerifyRsp           = 1103;
    public const ushort ItemNtf             = 1104;
    public const ushort HouseRandomReq      = 1109;
    public const ushort HouseRandomRsp      = 1110;
}
