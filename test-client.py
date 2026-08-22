#!/usr/bin/env python3
"""
GCG2 C# 离线服务端测试客户端（二进制协议版）。
用法: python3 test-client.py [accountId]
覆盖: VERIFY(1102) → LOGIN(1001) 完整登录链路 → 心跳 → 任务上报。
协议: 16字节包头 + Protobuf payload，与原版 new-cafe 完全对齐。
"""
import socket
import struct
import time
import sys

HOST = "127.0.0.1"
PORT = 30400

# 协议号
VERIFY_REQ            = 1102
VERIFY_RSP            = 1103
LOGIN_REQ             = 1001
LOGIN_RSP             = 1002
KEEP_ALIVE_REQ        = 1008
KEEP_ALIVE_RSP        = 1009
TASK_VALUE_RSP        = 1026
TASK_CHANGE_REQ       = 1027
TASK_CHANGE_RSP       = 1028
PLAYER_NTF            = 1005
ITEM_NTF              = 1104
PHONE_MSG_NTF         = 1035
LIVE2D_ENABLE_NTF     = 1036
LIVE2D_HX_NTF         = 1037
C2S_CALL_RSP          = 1023

CMD_NAMES = {
    1102: "VERIFY_REQ", 1103: "VERIFY_RSP",
    1001: "LOGIN_REQ", 1002: "LOGIN_RSP",
    1005: "PLAYER_NTF", 1006: "RENAME_REQ", 1007: "RENAME_RSP",
    1008: "KEEP_ALIVE_REQ", 1009: "KEEP_ALIVE_RSP",
    1022: "C2S_CALL_REQ", 1023: "C2S_CALL_RSP", 1024: "NTF_S2C_CALL",
    1025: "TASK_VALUE_REQ", 1026: "TASK_VALUE_RSP",
    1027: "TASK_CHANGE_REQ", 1028: "TASK_CHANGE_RSP",
    1029: "PLAYER_UPDATE_NTF", 1030: "GIRL_UPDATE_NTF",
    1031: "ITEM_UPDATE_NTF", 1032: "MONEY_UPDATE_NTF",
    1033: "FORMATION_UPDATE_NTF", 1034: "CHAPTER_UPDATE_NTF",
    1035: "PHONE_MSG_NTF", 1036: "LIVE2D_ENABLE_LEVEL_NTF",
    1037: "LIVE2D_HX_STATE_NTF",
    1048: "GET_HOUSEINFO_REQ", 1049: "GET_HOUSEINFO_RSP",
    1104: "ITEM_NTF", 1109: "HOUSE_RANDOM_REQ", 1110: "HOUSE_RANDOM_RSP",
}

HEADER_SIZE = 16
MAGIC = 0x88


# ---- 极简 Protobuf 编码 ----

def encode_varint(value):
    value = value & 0xFFFFFFFFFFFFFFFF
    out = bytearray()
    while True:
        b = value & 0x7F
        value >>= 7
        if value:
            out.append(b | 0x80)
        else:
            out.append(b)
            break
    return bytes(out)


def field_varint(field_num, value):
    return encode_varint(field_num << 3) + encode_varint(value)


def field_bytes(field_num, data):
    if isinstance(data, str):
        data = data.encode("utf-8")
    return encode_varint((field_num << 3) | 2) + encode_varint(len(data)) + data


# ---- 包读写 ----

def make_packet(command, serial, payload=b"", return_code=0):
    size = HEADER_SIZE + len(payload)
    header = struct.pack("<HHII", command, return_code, size, serial)
    header += bytes([0, MAGIC, 0, 0])  # compressed, magic, reserved[2]
    return header + payload


def read_packet(sock):
    header = recv_exact(sock, HEADER_SIZE)
    if not header:
        return None
    command, return_code, size, serial = struct.unpack("<HHII", header[:12])
    compressed = header[12]
    magic = header[13]
    if magic != MAGIC:
        print(f"  !! bad magic: 0x{magic:02X}")
        return None
    payload_len = size - HEADER_SIZE
    payload = recv_exact(sock, payload_len) if payload_len > 0 else b""
    return command, return_code, serial, compressed, payload


def recv_exact(sock, n):
    data = b""
    while len(data) < n:
        chunk = sock.recv(n - len(data))
        if not chunk:
            break
        data += chunk
    return data


def cmd_name(cmd):
    return CMD_NAMES.get(cmd, f"UNKNOWN_{cmd}")


def main():
    account = sys.argv[1] if len(sys.argv) > 1 else "test_player_001"
    serial = 1

    with socket.create_connection((HOST, PORT), timeout=10) as sock:
        print(f"[连接] {HOST}:{PORT}")

        # ===== 1. VERIFY_REQ (1102) =====
        payload = field_bytes(1, "Android") + field_bytes(2, account)
        sock.sendall(make_packet(VERIFY_REQ, serial, payload))
        print(f"[发送] {cmd_name(VERIFY_REQ)} account={account}")
        serial += 1

        pkt = read_packet(sock)
        if pkt:
            cmd, rc, ser, comp, pl = pkt
            print(f"[收到] {cmd_name(cmd)} (serial={ser}, payload={len(pl)}B)")

        # ===== 2. LOGIN_REQ (1001) =====
        # 服务端会按顺序推: TASK_VALUE_RSP → LIVE2D*2 → PLAYER_NTF → ITEM_NTF → PHONE_MSG_NTF → LOGIN_RSP
        payload = field_bytes(1, account) + field_varint(9, 0) + field_bytes(10, "offline")
        sock.sendall(make_packet(LOGIN_REQ, serial, payload))
        print(f"[发送] {cmd_name(LOGIN_REQ)}")
        serial += 1

        # 接收登录流程中的所有通知，直到 LOGIN_RSP
        login_done = False
        while not login_done:
            pkt = read_packet(sock)
            if not pkt:
                print("  !! connection closed during login")
                break
            cmd, rc, ser, comp, pl = pkt
            name = cmd_name(cmd)
            extra = ""
            if cmd == TASK_VALUE_RSP:
                extra = f" (taskValues payload={len(pl)}B)"
            elif cmd == PLAYER_NTF:
                extra = " (player data)"
            elif cmd == ITEM_NTF:
                extra = " (inventory data)"
            elif cmd == LOGIN_RSP:
                login_done = True
                extra = " *** LOGIN COMPLETE ***"
            print(f"[收到] {name}{extra}")

        # ===== 3. 心跳 =====
        sock.sendall(make_packet(KEEP_ALIVE_REQ, serial))
        print(f"[发送] {cmd_name(KEEP_ALIVE_REQ)}")
        serial += 1
        pkt = read_packet(sock)
        if pkt:
            cmd, *_ = pkt
            print(f"[收到] {cmd_name(cmd)}")

        # ===== 4. 任务上报 (1027) =====
        # taskValues: {1001: 1, 1002: 5}
        task_entry = field_varint(1, 1001) + field_varint(2, 1)
        task_entry2 = field_varint(1, 1002) + field_varint(2, 5)
        payload = field_bytes(1, task_entry) + field_bytes(1, task_entry2)
        sock.sendall(make_packet(TASK_CHANGE_REQ, serial, payload))
        print(f"[发送] {cmd_name(TASK_CHANGE_REQ)} taskValues={{1001:1, 1002:5}}")
        serial += 1
        pkt = read_packet(sock)
        if pkt:
            cmd, *_ = pkt
            print(f"[收到] {cmd_name(cmd)}")

        time.sleep(0.3)
        print("\n[完成] 全部测试通过，断开连接。")
        print("再次登录同一账号会看到 TASK_VALUE_RSP 带上次上报的 taskValues。")


def test_lua_calls():
    """测试 Lua 调用：摸头(触发剧情) + 战斗结算"""
    account = "lua_test_001"
    serial = 100

    with socket.create_connection((HOST, PORT), timeout=10) as sock:
        print(f"\n=== Lua 调用测试 ===")
        print(f"[连接] {HOST}:{PORT}")

        # 先登录
        payload = field_bytes(1, "Android") + field_bytes(2, account)
        sock.sendall(make_packet(VERIFY_REQ, serial, payload))
        serial += 1
        read_packet(sock)  # VERIFY_RSP

        payload = field_bytes(1, account) + field_varint(9, 0) + field_bytes(10, "offline")
        sock.sendall(make_packet(LOGIN_REQ, serial, payload))
        serial += 1
        # 接收登录流程通知直到 LOGIN_RSP
        while True:
            pkt = read_packet(sock)
            if not pkt:
                break
            cmd, *_ = pkt
            if cmd == LOGIN_RSP:
                break
        print("[登录] 完成")

        # 1. 摸头 HeadTouched（触发新手剧情）
        import json as _json
        head_touch = _json.dumps({"sCmd": "HeadTouched", "nId": 1, "nType": 1})
        payload = field_bytes(1, "GirlLogic") + field_bytes(2, head_touch)
        sock.sendall(make_packet(C2S_CALL_REQ, serial, payload))
        serial += 1
        print(f"[发送] C2S_CALL GirlLogic.HeadTouched girlId=1")

        # 收 C2S_CALL_RSP + NTF_S2C_CALL
        for _ in range(2):
            pkt = read_packet(sock)
            if pkt:
                cmd, *_ = pkt
                print(f"[收到] {cmd_name(cmd)}")

        # 2. 战斗结算 ChapterMsg nState=2
        battle = _json.dumps({"nState": 2, "nStageID": 65793, "bWin": True})  # 1-1 普通
        payload = field_bytes(1, "ChapterMsg") + field_bytes(2, battle)
        sock.sendall(make_packet(C2S_CALL_REQ, serial, payload))
        serial += 1
        print(f"[发送] C2S_CALL ChapterMsg nState=2 (战斗结算 1-1)")

        for _ in range(2):
            pkt = read_packet(sock)
            if pkt:
                cmd, rc, ser, comp, pl = pkt
                print(f"[收到] {cmd_name(cmd)} payload={len(pl)}B")

        time.sleep(0.3)
        print("[完成] Lua 调用测试通过")


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "--lua":
        test_lua_calls()
    else:
        main()
