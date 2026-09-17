#!/usr/bin/env bash
set -euo pipefail

# =============================================================================
# Game 服务器部署脚本（参考 GameFrameX.Admin.Unified.Api 部署约定）
#
# 通过 HTTP POST 触发远程部署 API，所有配置通过环境变量传入（由 CNB imports 提供）。
# 需要在 cnb-shared-config/envs.yml 中配置的变量：
#
#   公共（与 Admin 共用）:
#     DEPLOY_API_URL          — 部署 API 地址 (POST)
#     DEPLOY_AUTH_USERNAME    — Docker 认证用户名
#     DEPLOY_AUTH_PASSWORD    — Docker 认证密码
#     DEPLOY_AUTH_REGISTRY    — Docker 仓库地址
#     DEPLOY_AUTH_EMAIL       — Docker 认证邮箱
#     DEPLOY_NETWORK_NAME     — 网络名称 (共用)
#     DEPLOY_NETWORK_GATEWAY  — 网络网关 (共用)
#
#   Game 服务 (DEPLOY_GAME_ 前缀):
#     DEPLOY_GAME_CONTAINER_NAME  — 容器名称
#     DEPLOY_GAME_IP_ADDRESS      — 容器静态 IP 地址
#     DEPLOY_GAME_PORTS           — 端口映射 (JSON, 如 {"29100":"29100","28080":"28080"})
#     DEPLOY_GAME_VOLUMES         — 卷映射 (JSON, 如 {"/data/game/logs":"/app/data/logs"})
#     DEPLOY_GAME_ENVIRONMENTS    — 环境变量 (JSON)，至少包含:
#                                   GAMEFRAMEX_SERVER_ARGS — 完整启动参数（镜像入口原样透传，已验证组合：
#                                   --ServerType=Game --ServerId=1001 --ServerInstanceId=1
#                                   --InnerPort=29100 --OuterPort=29100 --HttpPort=28080 --WsPort=29110
#                                   --MetricsPort=29090 --MinModuleId=10 --MaxModuleId=9999
#                                   --IsEnableOnlineAdmin=true --OnlineTenantId=1 --OnlineAppId=1
#                                   --DataBaseUrl=<MongoDB 连接串> --DataBaseName=gameframex)
# =============================================================================

if [ -z "${DEPLOY_API_URL:-}" ]; then
  echo "[SKIP] DEPLOY_API_URL 未配置，跳过部署"
  exit 0
fi

if [ -z "${DEPLOY_GAME_CONTAINER_NAME:-}" ]; then
  echo "[SKIP] DEPLOY_GAME_CONTAINER_NAME 未配置，跳过 Game 部署"
  exit 0
fi

TAG="${IMAGE_TAG##*:}"
IMAGE_NAME="${IMAGE_TAG%:*}"

echo "[DEPLOY] 环境变量:"
echo "  IMAGE_TAG=${IMAGE_TAG}"
echo "  IMAGE_NAME=${IMAGE_NAME}"
echo "  TAG=${TAG}"
echo "  DEPLOY_API_URL=${DEPLOY_API_URL}"
echo "  DEPLOY_AUTH_USERNAME=${DEPLOY_AUTH_USERNAME:-}"
echo "  DEPLOY_AUTH_PASSWORD=****"
echo "  DEPLOY_AUTH_REGISTRY=${DEPLOY_AUTH_REGISTRY:-}"
echo "  DEPLOY_AUTH_EMAIL=${DEPLOY_AUTH_EMAIL:-}"
echo "  DEPLOY_NETWORK_NAME=${DEPLOY_NETWORK_NAME:-}"
echo "  DEPLOY_NETWORK_GATEWAY=${DEPLOY_NETWORK_GATEWAY:-}"
echo "  DEPLOY_GAME_IP_ADDRESS=${DEPLOY_GAME_IP_ADDRESS:-}"
echo "  DEPLOY_GAME_CONTAINER_NAME=${DEPLOY_GAME_CONTAINER_NAME:-}"
echo "  DEPLOY_GAME_PORTS=${DEPLOY_GAME_PORTS:-}"
echo "  DEPLOY_GAME_VOLUMES=${DEPLOY_GAME_VOLUMES:-}"
echo "  DEPLOY_GAME_ENVIRONMENTS=$(echo "${DEPLOY_GAME_ENVIRONMENTS:-}" | jq -c 'with_entries(.value = "****")' 2>/dev/null || true)"

validate_json() {
  local name="$1" value="$2"
  if ! echo "$value" | jq . > /dev/null 2>&1; then
    echo "[ERROR] ${name} 不是合法 JSON: ${value}" >&2
    exit 1
  fi
}

[ -z "${DEPLOY_GAME_PORTS:-}" ] && DEPLOY_GAME_PORTS="{}"
[ -z "${DEPLOY_GAME_VOLUMES:-}" ] && DEPLOY_GAME_VOLUMES="{}"
[ -z "${DEPLOY_GAME_ENVIRONMENTS:-}" ] && DEPLOY_GAME_ENVIRONMENTS="{}"

validate_json "DEPLOY_GAME_PORTS" "${DEPLOY_GAME_PORTS}"
validate_json "DEPLOY_GAME_VOLUMES" "${DEPLOY_GAME_VOLUMES}"
validate_json "DEPLOY_GAME_ENVIRONMENTS" "${DEPLOY_GAME_ENVIRONMENTS}"

DEPLOY_GAME_PORTS=$(echo "${DEPLOY_GAME_PORTS}" | jq -c .)
DEPLOY_GAME_VOLUMES=$(echo "${DEPLOY_GAME_VOLUMES}" | jq -c .)
DEPLOY_GAME_ENVIRONMENTS=$(echo "${DEPLOY_GAME_ENVIRONMENTS}" | jq -c .)

jq -n \
  --arg image_name "${IMAGE_NAME}" \
  --arg tag "${TAG}" \
  --arg username "${DEPLOY_AUTH_USERNAME}" \
  --arg password "${DEPLOY_AUTH_PASSWORD}" \
  --arg registry "${DEPLOY_AUTH_REGISTRY}" \
  --arg email "${DEPLOY_AUTH_EMAIL}" \
  --arg network_name "${DEPLOY_NETWORK_NAME}" \
  --arg subnet "${DEPLOY_NETWORK_GATEWAY%.*}.0" \
  --arg gateway "${DEPLOY_NETWORK_GATEWAY}" \
  --arg ip "${DEPLOY_GAME_IP_ADDRESS}" \
  --arg container "${DEPLOY_GAME_CONTAINER_NAME}" \
  --argjson ports "${DEPLOY_GAME_PORTS}" \
  --argjson volumes "${DEPLOY_GAME_VOLUMES}" \
  --argjson environments "${DEPLOY_GAME_ENVIRONMENTS}" \
'{
  ImageName: $image_name,
  Tag: $tag,
  IsRecreate: true,
  Auth: {
    Username: $username,
    Password: $password,
    Registry: $registry,
    Email: $email
  },
  NetworkConfig: {
    Name: $network_name,
    Subnet: $subnet,
    Gateway: $gateway,
    IpAddress: $ip,
    IsRecreate: false
  },
  ContainerName: $container,
  ports: $ports,
  volumes: $volumes,
  environments: $environments
}' > /tmp/deploy-body.json

echo "[DEPLOY] 部署 Game 服务器"
echo "[DEPLOY] 请求地址: ${DEPLOY_API_URL}"
echo "[DEPLOY] 请求参数:"
jq '.Auth.Password = "****"' /tmp/deploy-body.json
echo ""

HTTP_CODE=$(curl -s --max-time 180 -o /tmp/deploy-resp.txt -w "%{http_code}" -X POST "${DEPLOY_API_URL}" \
  -H "Content-Type: application/json; charset=UTF-8" \
  -d @/tmp/deploy-body.json)

echo "[DEPLOY] HTTP ${HTTP_CODE}"
echo "[DEPLOY] 响应内容:"
cat /tmp/deploy-resp.txt
echo ""

if [ "${HTTP_CODE}" -ge 200 ] && [ "${HTTP_CODE}" -lt 300 ]; then
  echo "[DEPLOY] Game 服务器部署成功"
else
  echo "[DEPLOY] Game 服务器部署失败 (HTTP ${HTTP_CODE})" >&2
  exit 1
fi
