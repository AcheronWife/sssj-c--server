#!/bin/bash
echo "========================================"
echo "  GCG2 离线服务端启动脚本"
echo "========================================"
echo ""

echo "[1/3] 配置 ADB 端口转发..."
adb reverse tcp:30400 tcp:30400 || echo "警告: ADB reverse 30400 失败"
adb reverse tcp:18080 tcp:18080 || echo "警告: ADB reverse 18080 失败"
echo "ADB 端口转发完成"
echo ""

echo "[2/3] 检查 .NET 环境..."
if ! command -v dotnet &> /dev/null; then
    echo "错误: 未找到 dotnet，请安装 .NET 8 SDK"
    exit 1
fi
echo ".NET 环境正常"
echo ""

echo "[3/3] 启动服务端..."
echo "HTTP: http://0.0.0.0:18080"
echo "TCP:  0.0.0.0:30400"
echo ""
echo "按 Ctrl+C 停止服务端"
echo "========================================"
echo ""

dotnet run --project Gcg2OfflineServer.csproj
