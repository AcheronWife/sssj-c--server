# GCG2 离线服务端

《蔚蓝档案》(Blue Archive / GCG2) 离线研究服务端，基于 .NET 8 / C# 开发。

## 功能特性

- TCP 游戏网关（端口 30400），二进制协议 + Protobuf payload
- HTTP 服务器列表（端口 18080），`/health`、`/serverlist`、`/serverstate`
- 完整登录流程：VERIFY(1102) → LOGIN(1001) → 玩家数据/背包/任务/短信推送
- 玩家数据持久化（`data/state.json`，原子写入）
- Lua 调用分发（sCmd 路由），支持主线关卡战斗结算、咖啡馆、抽卡、商店、养成等
- GM 命令系统（通过好友搜索 roleid=11375 触发一键满配）
- 结构化日志（终端 + 文件双写）

## 环境要求

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- 对应版本的游戏客户端（需自行配置服务器地址）

## 快速开始

```bash
# 还原依赖
dotnet restore

# 开发运行
dotnet run

# 生产构建
dotnet build -c Release
dotnet bin/Release/net8.0/Gcg2OfflineServer.dll
```

启动成功输出：
```
TCP gateway listening on 0.0.0.0:30400
HTTP server starting on http://0.0.0.0:18080
Ready.
```

## GM 命令

游戏内添加好友，搜索角色 ID `11375`，即可触发一键满配：
- 等级 80、体力 999、金币 9999999、钻石 99999
- 全部货币拉满
- 解锁全部关卡
- 全部角色卡（从抽卡池读取）
- 全部武器（从武器表读取）
- 满级模块

## 配置

`appsettings.json`：

```json
{
  "http": { "host": "0.0.0.0", "port": 18080 },
  "gateway": { "host": "0.0.0.0", "port": 30400, "advertisedHost": "127.0.0.1" },
  "serverList": {
    "id": 1, "aid": 1, "sid": 1,
    "name": "离线研究服", "state": 1, "level": 1
  }
}
```

环境变量覆盖：`GCG_HTTP_HOST`、`GCG_HTTP_PORT`、`GCG_GATEWAY_HOST`、`GCG_GATEWAY_PORT`、`GCG_GAME_HOST`、`GCG_GM_TOKEN`。

## 手机连接（ADB 反向转发）

```cmd
adb reverse tcp:18080 tcp:18080
adb reverse tcp:30400 tcp:30400
```

## 项目结构

```
gcg2-csharp-server/
├── Gcg2OfflineServer.csproj
├── Program.cs                 # 入口 + HTTP 路由 + TCP 网关启动
├── appsettings.json
├── Protocol/
│   ├── Command.cs             # 协议号定义
│   ├── GamePacket.cs          # 16 字节包头读写
│   ├── ProtobufCodec.cs       # Protobuf varint/bytes 编解码
│   ├── MessageFactory.cs      # 消息体编码
│   └── LuaDispatcher.cs       # Lua 调用分发（sCmd 路由）
├── Models/
│   └── PlayerState.cs         # 玩家数据模型
├── GameData/
│   ├── GameDefaults.cs        # 默认玩家数据
│   ├── ChapterConfig.cs       # 关卡配置
│   ├── ExtraGameData.cs       # 武器/角色卡/活动等扩展数据
│   └── GuideMissionData.cs    # 引导任务配置
├── Services/
│   ├── GameLogger.cs          # 日志
│   ├── PlayerRepository.cs    # 玩家仓库 + 持久化
│   └── TcpGateway.cs          # TCP 网关
├── resources/                 # 游戏资源数据（关卡表、武器表、抽卡池等）
├── data/                      # 运行时生成 state.json
└── logs/                      # 运行时生成 server.log
```

## TCP 协议格式

二进制帧，全部小端：

```
偏移  长度  字段
0     2     command      协议号
2     2     returnCode   返回码
4     4     size         总包长 = 16 + payload 长度
8     4     serial       序列号
12    1     compressed   压缩标记(0=未压缩)
13    1     magic        固定 0x88
14    2     reserved     保留(0)
16    N     payload      Protobuf 编码
```

## 已知问题

本项目为研究性质，存在以下已知问题：

- 部分 Lua 命令未实现（返回空响应）
- 战斗结算为简化实现，掉落奖励为随机生成
- 部分活动/限时玩法未实现
- 客户端版本兼容性问题，需使用对应版本的 APK
- 武器被动技能数据未完整实现

## 免责声明

本项目仅用于技术研究和学习，请勿用于商业用途。游戏资源及相关数据版权归原公司所有。
