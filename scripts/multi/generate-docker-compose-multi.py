#!/usr/bin/env python3
"""docker-compose.multi.yml 拓扑环境变量生成器（C143f Stage-6 D12/D13）。

按拓扑定义（Role 名 / 实例数 / 端口基址）展开每个实例，并注入：

- ``services__{Role}__tcp__0`` / ``services__{Role}_{InstanceId}__tcp__0``：
  AspireEndpointResolver 静态引导契约（全实例共享同一张映射）。
- ``GameFrameX__AdvertiseHost`` / ``GameFrameX__AdvertisePort`` / ``GameFrameX__RoleInstanceId``：
  MongoEndpointRegistry 地址自报引导变量（逐实例注入，D12）。

端口编号方案与退役前的静态形态（现保留为 docker-compose.multi.legacy.yml）逐实例一致，
生成结果直接写入 docker-compose.multi.yml；重复执行输出幂等。

用法::

    python3 scripts/multi/generate-docker-compose-multi.py            # 写入 docker-compose.multi.yml
    python3 scripts/multi/generate-docker-compose-multi.py --check    # 校验已提交文件与生成结果一致（CI 冒烟）
"""

from __future__ import annotations

import argparse
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


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate docker-compose.multi.yml from the topology definition.")
    parser.add_argument("--check", action="store_true", help="verify the committed file matches the generated output")
    parser.add_argument("--output", type=pathlib.Path, default=OUTPUT_PATH, help="output path (default: docker-compose.multi.yml)")
    args = parser.parse_args()

    all_instances = []
    role_instances = {}
    for role in ROLES:
        instances = build_instances(role)
        role_instances[role["name"]] = instances
        all_instances.extend(instances)

    discovery_env_lines = build_discovery_env(all_instances)

    out = [HEADER, "name: gfx-multi", "", "x-launcher-discovery-env: &launcher-discovery-env"]
    out += discovery_env_lines
    out += [
        "",
        "services:",
        "  mongo:",
        "    image: mongo:8.2",
        "    restart: unless-stopped",
        "    ports:",
        f"      - \"{MONGO_HOST_PORT}:27017\"",
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

    social_services = [i["service"] for i in role_instances["Social"]]

    first = True
    for role in ROLES:
        for instance in role_instances[role["name"]]:
            out += render_service(instance, build_image=first)
            first = False
            out += [
                "    depends_on:",
                "      mongo:",
                "        condition: service_healthy",
            ]
            if role["name"] == "Game":
                for social_service in social_services:
                    out += [
                        f"      {social_service}:",
                        "        condition: service_started",
                    ]
            out += [
                "    command:",
                f"      - \"--ServerType={role['name']}\"",
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
                "    networks:",
                "      - gameframex-multi",
                "",
            ]

    out += [
        "networks:",
        "  gameframex-multi:",
        "    driver: bridge",
        "",
    ]

    content = "\n".join(out)

    if args.check:
        existing = args.output.read_text() if args.output.exists() else ""
        if existing != content:
            print(f"[FAIL] {args.output.name} 与生成结果不一致，请运行生成器更新后提交")
            return 1
        print(f"[OK] {args.output.name} 与生成结果一致")
        return 0

    args.output.write_text(content)
    print(f"[OK] 已写入 {args.output}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
