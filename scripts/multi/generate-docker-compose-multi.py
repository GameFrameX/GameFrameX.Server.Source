#!/usr/bin/env python3
"""docker-compose.multi.yml 拓扑环境变量生成器（C143f Stage-6 D12/D13）。

按拓扑定义（Role 名 / 实例数 / 端口基址）展开每个实例，并注入：

- ``services__{Role}__tcp__0`` / ``services__{Role}_{InstanceId}__tcp__0``：
  AspireEndpointResolver 静态引导契约（全实例共享同一张映射）。
- ``GameFrameX__AdvertiseHost`` / ``GameFrameX__AdvertisePort`` / ``GameFrameX__RoleInstanceId``：
  MongoEndpointRegistry 地址自报引导变量（逐实例注入，D12）。

每个实例同时生成一份与其 command 参数值完全一致的独立配置段
（``Configs/multi/{service}.json``），并挂载到容器内 ``/app/Configs/app_config.json``：
文件层与 CLI 层单源一致，ConfigStartupValidator 的冲突校验保持启用且必然放行。

端口编号方案与退役前的静态形态（现保留为 docker-compose.multi.legacy.yml）逐实例一致，
生成结果直接写入 docker-compose.multi.yml 与 Configs/multi/；重复执行输出幂等。
未认证的 MongoDB 仅绑定 127.0.0.1，避免向外部接口暴露可读写的数据面。

用法::

    python3 scripts/multi/generate-docker-compose-multi.py            # 写入 docker-compose.multi.yml 与 Configs/multi/
    python3 scripts/multi/generate-docker-compose-multi.py --check    # 校验已提交文件与生成结果一致（CI 冒烟）
"""

from __future__ import annotations

import argparse
import json
import pathlib
import sys

# ----------------------------------------------------------------------------------
# 拓扑定义：mongo + social ×2 + game ×10（与 legacy 静态形态一致）
# 每个 Role：name / server_id_base / instance_id_base / count / 端口基址与步进
# ----------------------------------------------------------------------------------
ROLES = [
    {
        "name": "Social",
        "server_id_base": 5531,
        "instance_id_base": 2001,
        "count": 2,
        "inner_port_base": 29400,
        "http_port_base": 28081,
        "host_inner_port_base": 49400,
        "host_http_port_base": 48081,
        "port_stride": 2,
    },
    {
        "name": "Game",
        "server_id_base": 1001,
        "instance_id_base": 1001,
        "count": 10,
        "inner_port_base": 29100,
        "http_port_base": 28080,
        "host_inner_port_base": 49100,
        "host_http_port_base": 48080,
        "port_stride": 2,
    },
]

MONGO_HOST_PORT = 47017
DATABASE_URL = "mongodb://mongo:27017/?authSource=admin"
DATABASE_NAME = "gameframex_multi"

REPO_ROOT = pathlib.Path(__file__).resolve().parent.parent.parent
OUTPUT_PATH = REPO_ROOT / "docker-compose.multi.yml"
CONFIG_OUTPUT_DIR = REPO_ROOT / "Configs" / "multi"

HEADER = """\
# 本文件由 scripts/multi/generate-docker-compose-multi.py 生成（C143f Stage-6 拓扑环境变量生成器）。
# 修改拓扑请编辑该脚本的 ROLES 定义后重新生成，不要手改本文件。
# 现有静态形态保留在 docker-compose.multi.legacy.yml（过渡期兼容）。
"""


def build_instances(role: dict) -> list[dict]:
    """把一个 Role 的拓扑定义展开为逐实例参数表。"""
    instances = []
    for offset in range(role["count"]):
        inner_port = role["inner_port_base"] + offset
        instances.append(
            {
                "role": role["name"],
                "service": f"{role['name'].lower()}-{offset + 1}",
                "server_id": role["server_id_base"] + offset,
                "instance_id": role["instance_id_base"] + offset,
                "inner_port": inner_port,
                "http_port": role["http_port_base"] + offset * role["port_stride"],
                "host_inner_port": role["host_inner_port_base"] + offset,
                "host_http_port": role["host_http_port_base"] + offset * role["port_stride"],
            }
        )
    return instances


def build_discovery_env(all_instances: list[dict]) -> list[str]:
    """构建 services__* 静态引导映射（Role 级指向首实例，实例级逐实例展开）。

    输出顺序固定（Role 内按 instance_id 升序，Role 级键排在同名实例键之后），
    保证重复生成的文件字节级幂等。
    """
    lines = []
    role_order = []
    for instance in all_instances:
        if instance["role"] not in role_order:
            role_order.append(instance["role"])
    for role in role_order:
        role_instances = [i for i in all_instances if i["role"] == role]
        for instance in role_instances:
            lines.append(
                f"  services__{role}_{instance['instance_id']}__tcp__0: \"tcp://{instance['service']}:{instance['inner_port']}\""
            )
        first = role_instances[0]
        lines.append(f"  services__{role}__tcp__0: \"tcp://{first['service']}:{first['inner_port']}\"")
    return lines


def render_instance_config(instance: dict) -> str:
    """渲染单实例配置段（列表形态，与 Configs/app_config.json 的多段结构同构）。

    字段与值必须与下方 command 参数一一对应：ConfigStartupValidator 会比较实际出现的
    CLI 字段与文件段值，二者不一致即启动前 fail fast，因此这里与 command 同源生成。
    """
    section = {
        "ServerType": instance["role"],
        "ServerId": instance["server_id"],
        "ServerInstanceId": instance["instance_id"],
        "InnerHost": "0.0.0.0",
        "InnerPort": instance["inner_port"],
        "OuterHost": "0.0.0.0",
        "OuterPort": instance["inner_port"],
        "HttpPort": instance["http_port"],
        "IsEnableHttp": True,
        "DataBaseUrl": DATABASE_URL,
        "DataBaseName": DATABASE_NAME,
    }
    return json.dumps([section], indent=2, ensure_ascii=False) + "\n"


def render_service(instance: dict, build_image: bool) -> list[str]:
    """渲染单个实例的 service 定义头（服务名 + 可选构建段 + 镜像）。"""
    lines = [f"  {instance['service']}:"]
    if build_image:
        lines += [
            "    build:",
            "      context: .",
        ]
    lines.append("    image: gameframex/server.launcher:local")
    return lines


def expand_role_instances() -> tuple[list[dict], dict[str, list[dict]]]:
    """把 ROLES 拓扑定义展开为逐实例参数表。

    返回 ``(all_instances, role_instances)``：前者按 ROLES 定义顺序平铺全部实例，
    后者按 Role 名分组，供发现映射与 service 段渲染分别取用。
    """
    all_instances = []
    role_instances = {}
    for role in ROLES:
        instances = build_instances(role)
        role_instances[role["name"]] = instances
        all_instances.extend(instances)
    return all_instances, role_instances


def render_mongo_service() -> list[str]:
    """渲染 mongo service 定义段（含前置空行与 ``services:`` 头，保持字节级顺序）。"""
    return [
        "",
        "services:",
        "  mongo:",
        "    image: mongo:8.2",
        "    restart: unless-stopped",
        "    ports:",
        f"      - \"127.0.0.1:{MONGO_HOST_PORT}:27017\"",
        "    healthcheck:",
        "      test: [ \"CMD\", \"mongosh\", \"--quiet\", \"--eval\", \"db.runCommand({ ping: 1 }).ok\" ]",
        "      interval: 5s",
        "      timeout: 3s",
        "      retries: 20",
        "    volumes:",
        "      - \"./running-multi/mongo:/data/db\"",
        "    networks:",
        "      - gameframex-multi",
    ]


def render_instance_service_body(instance: dict, role_name: str, social_services: list[str]) -> list[str]:
    """渲染单实例 service 定义体（depends_on + command + environment + ports + volumes + networks）。

    Game 角色额外按 ``social_services`` 顺序附加对全部 Social 实例的
    ``service_started`` 依赖；字段值必须与 ``render_instance_config`` 的文件段同源一致。
    """
    lines = [
        "    depends_on:",
        "      mongo:",
        "        condition: service_healthy",
    ]
    if role_name == "Game":
        for social_service in social_services:
            lines += [
                f"      {social_service}:",
                "        condition: service_started",
            ]
    lines += [
        "    command:",
        f"      - \"--ServerType={role_name}\"",
        f"      - \"--ServerId={instance['server_id']}\"",
        f"      - \"--ServerInstanceId={instance['instance_id']}\"",
        "      - \"--InnerHost=0.0.0.0\"",
        f"      - \"--InnerPort={instance['inner_port']}\"",
        "      - \"--OuterHost=0.0.0.0\"",
        f"      - \"--OuterPort={instance['inner_port']}\"",
        f"      - \"--HttpPort={instance['http_port']}\"",
        "      - \"--IsEnableHttp=true\"",
        f"      - \"--DataBaseUrl={DATABASE_URL}\"",
        f"      - \"--DataBaseName={DATABASE_NAME}\"",
        "    environment:",
        "      <<: *launcher-discovery-env",
        f"      GameFrameX__AdvertiseHost: {instance['service']}",
        f"      GameFrameX__AdvertisePort: \"{instance['inner_port']}\"",
        f"      GameFrameX__RoleInstanceId: \"{instance['instance_id']}\"",
        "    ports:",
        f"      - \"{instance['host_inner_port']}:{instance['inner_port']}\"",
        f"      - \"{instance['host_http_port']}:{instance['http_port']}\"",
        "    volumes:",
        f"      - \"./running-multi/{instance['service']}/logs:/app/data/logs\"",
        "      - \"./GameFrameX.Config/json:/app/Configs:ro\"",
        f"      - \"./Configs/multi/{instance['service']}.json:/app/Configs/app_config.json:ro\"",
        "    networks:",
        "      - gameframex-multi",
        "",
    ]
    return lines


def render_launcher_services(role_instances: dict[str, list[dict]]) -> list[str]:
    """按 ROLES 顺序渲染全部 launcher 实例的 service 段（仅首个实例附加 build 段）。"""
    social_services = [i["service"] for i in role_instances["Social"]]
    out = []
    first = True
    for role in ROLES:
        for instance in role_instances[role["name"]]:
            out += render_service(instance, build_image=first)
            first = False
            out += render_instance_service_body(instance, role["name"], social_services)
    return out


def verify_outputs(output_path: pathlib.Path, content: str, config_outputs: dict[pathlib.Path, str]) -> int:
    """校验已提交的 compose 文件与实例配置段是否与生成结果一致（``--check`` 模式）。

    任一不一致打印 ``[FAIL]`` 明细并返回 1；全部一致打印 ``[OK]`` 并返回 0。
    """
    ok = True
    existing = output_path.read_text() if output_path.exists() else ""
    if existing != content:
        print(f"[FAIL] {output_path.name} 与生成结果不一致，请运行生成器更新后提交")
        ok = False

    committed = {path.name for path in CONFIG_OUTPUT_DIR.glob("*.json")} if CONFIG_OUTPUT_DIR.exists() else set()
    expected = {path.name for path in config_outputs}
    if committed != expected:
        print(
            f"[FAIL] {CONFIG_OUTPUT_DIR.name}/ 与生成结果不一致"
            f"（多余: {sorted(committed - expected)}, 缺失: {sorted(expected - committed)}）"
        )
        ok = False
    else:
        for path, expected_content in config_outputs.items():
            if path.read_text() != expected_content:
                print(f"[FAIL] {path.name} 与生成结果不一致，请运行生成器更新后提交")
                ok = False
    if not ok:
        return 1

    print(f"[OK] {output_path.name} 与生成结果一致")
    print(f"[OK] {CONFIG_OUTPUT_DIR.name}/ 共 {len(config_outputs)} 份实例配置与生成结果一致")
    return 0


def write_outputs(output_path: pathlib.Path, content: str, config_outputs: dict[pathlib.Path, str]) -> int:
    """写出 compose 文件与逐实例配置段（默认模式）。"""
    output_path.write_text(content)
    print(f"[OK] 已写入 {output_path}")
    CONFIG_OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    for path, config_content in config_outputs.items():
        path.write_text(config_content)
        print(f"[OK] 已写入 {path}")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate docker-compose.multi.yml from the topology definition.")
    parser.add_argument("--check", action="store_true", help="verify the committed file matches the generated output")
    parser.add_argument("--output", type=pathlib.Path, default=OUTPUT_PATH, help="output path (default: docker-compose.multi.yml)")
    args = parser.parse_args()

    all_instances, role_instances = expand_role_instances()

    out = [HEADER, "name: gfx-multi", "", "x-launcher-discovery-env: &launcher-discovery-env"]
    out += build_discovery_env(all_instances)
    out += render_mongo_service()
    out += render_launcher_services(role_instances)
    out += [
        "networks:",
        "  gameframex-multi:",
        "    driver: bridge",
        "",
    ]

    content = "\n".join(out)

    config_outputs = {
        CONFIG_OUTPUT_DIR / f"{instance['service']}.json": render_instance_config(instance)
        for instance in all_instances
    }

    if args.check:
        return verify_outputs(args.output, content, config_outputs)
    return write_outputs(args.output, content, config_outputs)


if __name__ == "__main__":
    sys.exit(main())
