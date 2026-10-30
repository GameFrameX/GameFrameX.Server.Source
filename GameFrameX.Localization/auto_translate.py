#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
自动翻译脚本 - 分析代码上下文并自动生成翻译
"""

import os
import re
import subprocess
from pathlib import Path
from collections import defaultdict

# 项目路径
SCRIPT_DIR = Path(__file__).parent
PROJECT_ROOT = SCRIPT_DIR.parent
KEYS_DIR = SCRIPT_DIR
RESX_DIR = SCRIPT_DIR / "Localization" / "Messages"

# resx 文件路径
RESX_DEFAULT = RESX_DIR / "Resources.resx"
RESX_ZH_CN = RESX_DIR / "Resources.zh-CN.resx"

# 待翻译占位符
TODO_PLACEHOLDER = '[TODO]'


def get_todo_keys(resx_path, placeholder=TODO_PLACEHOLDER):
    """获取带指定占位符标记的 key"""
    keys = []
    with open(resx_path, 'r', encoding='utf-8') as f:
        content = f.read()

    # 匹配 <data name="xxx"><value>[TODO/待翻译] xxx</value>
    pattern = re.compile(rf'<data name="([^"]+)"[^>]*>\s*<value>{re.escape(placeholder)} [^<]*</value>', re.DOTALL)
    for match in pattern.finditer(content):
        keys.append(match.group(1))
    return keys


def search_key_usage(key):
    """搜索 key 在代码中的使用位置，返回上下文"""
    # 搜索 Localization.Keys.xxx 或 Keys.xxx 的使用
    key_parts = key.split('.')
    search_patterns = [
        f"Localization.Keys.{key}",
        f"Keys.{key}",
    ]

    results = []
    for pattern in search_patterns:
        try:
            # 使用 grep 搜索
            cmd = ['grep', '-r', '-n', '-B', '2', '-A', '2', pattern,
                   '--include=*.cs', str(PROJECT_ROOT)]
            result = subprocess.run(cmd, capture_output=True, text=True, timeout=10)
            if result.stdout:
                results.append(result.stdout)
        except Exception as e:
            pass

    return '\n'.join(results)


def analyze_key_context(key, context):
    """分析 key 的上下文，推断其含义"""
    key_lower = key.lower()
    context_lower = context.lower()

    # 提取关键信息
    info = {
        'is_exception': 'exception' in key_lower or 'throw' in context_lower,
        'is_log': 'log' in key_lower or 'loghelper' in context_lower,
        'is_warning': 'warning' in key_lower or 'log.warning' in context_lower,
        'is_error': 'error' in key_lower or 'log.error' in context_lower or 'loghelper.error' in context_lower,
        'is_info': 'info' in key_lower or 'log.info' in context_lower or 'loghelper.info' in context_lower,
        'is_debug': 'debug' in key_lower or 'log.debug' in context_lower or 'loghelper.debug' in context_lower,
        'is_success': 'success' in key_lower or 'complete' in key_lower,
        'is_failed': 'fail' in key_lower or 'error' in key_lower,
        'is_start': 'start' in key_lower,
        'is_end': 'end' in key_lower or 'complete' in key_lower or 'finish' in key_lower,
        'is_timeout': 'timeout' in key_lower,
        'is_connect': 'connect' in key_lower,
        'is_disconnect': 'disconnect' in key_lower,
        'has_params': '{0}' in context or '{1}' in context,
    }

    return info


def generate_translation(key, context, info):
    """根据 key 名称和上下文生成翻译"""
    translations = {
        # ===== DiscoveryCenterManager.Startup =====
        'DiscoveryCenterManager.Startup.ServiceOfflineCallback': (
            'Service offline callback executed. Service type: {0}, server ID: {1}.',
            '服务下线回调已执行。服务类型：{0}，服务器ID：{1}。'
        ),
        'DiscoveryCenterManager.Startup.ServiceOnlineCallback': (
            'Service online callback executed. Service type: {0}, server ID: {1}.',
            '服务上线回调已执行。服务类型：{0}，服务器ID：{1}。'
        ),

        # ===== Exceptions.BigInteger =====
        'Exceptions.BigInteger.ConstructorByteOverflow': (
            'BigInteger constructor byte overflow.',
            'BigInteger构造函数字节溢出。'
        ),
        # ===== Exceptions.Database =====
        # ===== Exceptions.Handler =====
        # ===== Exceptions.Network =====
        # ===== Exceptions.Timer =====
        'Exceptions.Timer.InvalidParameters': (
            'Timer invalid parameters. Parameter: {0}, value: {1}.',
            '计时器参数无效。参数：{0}，值：{1}。'
        ),
        # ===== Logs.Client =====
        # ===== Logs.Database =====
        # ===== Logs.Discovery =====
        # ===== Logs.Game.Server =====
        # ===== Logs.Network =====
        # ===== Logs.Server =====
        # ===== StartUp.ApplicationSettings =====
        # ===== StartUp.DiscoveryCenter =====
        'StartUp.DiscoveryCenter.HostNotConfigured': (
            'Discovery center host not configured.',
            '发现中心主机地址未配置。'
        ),
        'StartUp.DiscoveryCenter.InvalidHostAddress': (
            'Invalid discovery center host address: {0}.',
            '无效的发现中心主机地址：{0}。'
        ),
        'StartUp.DiscoveryCenter.PortNotConfigured': (
            'Discovery center port not configured.',
            '发现中心端口未配置。'
        ),

        # ===== StartUp =====
        'StartUp.GrafanaLokiLabelExists': (
            'Grafana Loki label already exists. Label: {0}.',
            'Grafana Loki标签已存在。标签：{0}。'
        ),
        'StartUp.HealthCheck.EndpointEnabled': (
            'Health check endpoint enabled at: {0}.',
            '健康检查端点已启用：{0}。'
        ),
        'StartUp.HealthCheck.Ok': (
            'OK',
            '正常'
        ),
        # ===== StartUp.HttpExceptions =====
        'StartUp.HttpExceptions.AddressMustEndWithSlash': (
            'Address must end with slash. Address: {0}.',
            '地址必须以斜杠结尾。地址：{0}。'
        ),
        'StartUp.HttpExceptions.AddressMustStartWithSlash': (
            'Address must start with slash. Address: {0}.',
            '地址必须以斜杠开头。地址：{0}。'
        ),
        'StartUp.HttpExceptions.HttpsNotImplemented': (
            'HTTPS is not implemented yet.',
            'HTTPS尚未实现。'
        ),

        # ===== StartUp.HttpServer =====
        'StartUp.HttpServer.ApiContactName': (
            'API contact name: {0}.',
            'API联系人名称：{0}。'
        ),
        'StartUp.HttpServer.ApiDescription': (
            'API description: {0}.',
            'API描述：{0}。'
        ),
        'StartUp.HttpServer.ApiLicenseName': (
            'API license name: {0}.',
            'API许可证名称：{0}。'
        ),
        'StartUp.HttpServer.ApiTitle': (
            'API title: {0}.',
            'API标题：{0}。'
        ),
        'StartUp.HttpServer.PortOccupied': (
            'HTTP server port is occupied. Port: {0}.',
            'HTTP服务器端口被占用。端口：{0}。'
        ),
        'StartUp.HttpServer.StartupComplete': (
            'HTTP server startup complete. Listening on: {0}.',
            'HTTP服务器启动完成。监听地址：{0}。'
        ),
        'StartUp.HttpServer.SwaggerEndpointFormat': (
            'Swagger endpoint format: {0}.',
            'Swagger端点格式：{0}。'
        ),
        'StartUp.HttpServer.SwaggerRoutePrefix': (
            'Swagger route prefix: {0}.',
            'Swagger路由前缀：{0}。'
        ),

        # ===== StartUp =====
        'StartUp.LaunchServerType': (
            'Launching server type: {0}.',
            '启动服务器类型：{0}。'
        ),
        'StartUp.NoConfigurationUseDefault': (
            'No configuration found, using default values.',
            '未找到配置，使用默认值。'
        ),
        'StartUp.PrometheusMetricsEndpointEnabledInline': (
            'Prometheus metrics endpoint enabled inline.',
            'Prometheus指标端点已内联启用。'
        ),
        'StartUp.StartingServerWithConfiguration': (
            'Starting server with configuration: {0}.',
            '使用配置启动服务器：{0}。'
        ),
        'StartUp.StartupOver': (
            'Server startup completed.',
            '服务器启动完成。'
        ),

        # ===== Utility.CompressionHelper =====
        'Utility.CompressionHelper.ExceptionError': (
            'Compression helper exception. Error: {0}.',
            '压缩辅助类异常。错误：{0}。'
        ),

        # ===== Utility.Exceptions =====
        'Utility.Exceptions.ClockMovedBackwards': (
            'System clock moved backwards. Refusing to generate id for {0} milliseconds.',
            '系统时钟回退。拒绝生成ID {0} 毫秒。'
        ),
        'Utility.Exceptions.ConstructorByteOverflow': (
            'BigInteger constructor byte overflow.',
            'BigInteger构造函数字节溢出。'
        ),
        'Utility.Exceptions.LnValueNonPositive': (
            'Cannot calculate natural logarithm of non-positive value.',
            '无法计算非正值的自然对数。'
        ),
        'Utility.Exceptions.SqrtValueNegative': (
            'Cannot calculate square root of negative value.',
            '无法计算负值的平方根。'
        ),
        'Utility.Exceptions.ValueOutOfRange': (
            'Value out of range. Value: {0}, min: {1}, max: {2}.',
            '值超出范围。值：{0}，最小：{1}，最大：{2}。'
        ),
        # ===== Utility.GlobalSettings =====
        'Utility.GlobalSettings.LoadGlobalSettings': (
            'Loading global settings from configuration.',
            '从配置加载全局设置。'
        ),
        'Utility.GlobalSettings.LoadGlobalSettingsFailed': (
            'Failed to load global settings. Error: {0}.',
            '加载全局设置失败。错误：{0}。'
        ),

        # ===== Utility.Settings =====
        'Utility.Settings.LoadConfigurationFailed': (
            'Failed to load configuration. Configuration file: {0}.',
            '加载配置失败。配置文件：{0}。'
        ),
    }

    return translations.get(key, None)


def update_resx_file(resx_path, updates, is_chinese=False):
    """更新 resx 文件中的翻译"""
    with open(resx_path, 'r', encoding='utf-8') as f:
        content = f.read()

    updated_count = 0

    for key, (en_value, zh_value) in updates.items():
        value = zh_value if is_chinese else en_value
        placeholder = '[待翻译]' if is_chinese else TODO_PLACEHOLDER

        # 使用正则表达式替换
        # 匹配 <data name="key"...><value>[TODO/待翻译] ...</value>
        pattern = re.compile(
            rf'(<data name="{re.escape(key)}"[^>]*>\s*<value>){re.escape(placeholder)}[^<]*(</value>)',
            re.DOTALL
        )

        def replace_func(match):
            nonlocal updated_count
            updated_count += 1
            return match.group(1) + value + match.group(2)

        content = pattern.sub(replace_func, content)

    if updated_count > 0:
        with open(resx_path, 'w', encoding='utf-8') as f:
            f.write(content)

    return updated_count


def build_translations(todo_keys, translations):
    """为待翻译 key 搜索上下文并补充翻译规则，结果写回共享的 translations 字典。"""
    for key in todo_keys:
        if key in translations:
            continue
        context = search_key_usage(key)
        info = analyze_key_context(key, context)
        result = generate_translation(key, context, info)
        if result:
            translations[key] = result
            print(f"   [OK] {key}")
        else:
            print(f"   [SKIP] {key} - 未找到翻译规则")


def main():
    print("=" * 60)
    print("自动翻译脚本 - 分析代码并生成翻译")
    print("=" * 60)
    print()

    # 翻译字典（用于两个文件）
    translations = {}

    # 1. 处理英文版
    print("1. 处理英文资源文件 (Resources.resx)...")
    todo_keys_en = get_todo_keys(RESX_DEFAULT, TODO_PLACEHOLDER)
    print(f"   找到 {len(todo_keys_en)} 个需要翻译的 key")

    if todo_keys_en:
        build_translations(todo_keys_en, translations)
        if translations:
            updated_en = update_resx_file(RESX_DEFAULT, translations, is_chinese=False)
            print(f"   英文资源文件更新了 {updated_en} 条")
    else:
        print("   没有需要翻译的 key")

    # 2. 处理中文版
    print()
    print("2. 处理中文资源文件 (Resources.zh-CN.resx)...")
    todo_keys_zh = get_todo_keys(RESX_ZH_CN, '[待翻译]')
    print(f"   找到 {len(todo_keys_zh)} 个需要翻译的 key")

    if todo_keys_zh:
        build_translations(todo_keys_zh, translations)
        if translations:
            updated_zh = update_resx_file(RESX_ZH_CN, translations, is_chinese=True)
            print(f"   中文资源文件更新了 {updated_zh} 条")
    else:
        print("   没有需要翻译的 key")

    print()
    print("=" * 60)
    print("翻译完成!")
    print("=" * 60)


if __name__ == "__main__":
    main()
