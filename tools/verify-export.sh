#!/usr/bin/env sh
# 发布断言：导出构件里不应出现开发期工具链或仓库根配置。
# 用法: tools/release/verify-export.sh builds/windows/AutoCMEX.exe
set -u

artifact=${1:-}
if [ -z "$artifact" ] || [ ! -f "$artifact" ]; then
  echo "usage: $0 <exported-artifact>"
  exit 2
fi

fail=0

check() { # label, needle
  label=$1; needle=$2
  n=$(grep -a -c -F -- "$needle" "$artifact" 2>/dev/null || true)
  if [ "${n:-0}" -ne 0 ]; then
    echo "verify-export: $label ($n hits)"
    fail=1
  fi
}

check "dev autoload baked in" "autoload/MCPRuntime"
check "dev MCP runtime script present" "mcp_runtime_autoload"
check "root config cspell.json" "cspell.json"
check "root config skills-lock.json" "skills-lock.json"
check "root config omnisharp.json" "omnisharp.json"
check "root config renovate.json" "chickensoft-games/renovate"
check "root config global.json" '"msbuild-sdks"'

if [ "$fail" -ne 0 ]; then
  echo "verify-export: FAILED"
  exit 1
fi
echo "verify-export: OK"
