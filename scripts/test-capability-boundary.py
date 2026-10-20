#!/usr/bin/env python3
"""用真实 MSBuild ProjectReference / 本地 NuGet 验证 GFX0017（不修改工作区项目）。"""
import os
from pathlib import Path
import subprocess
import tempfile
from xml.sax.saxutils import escape

REPO = Path(__file__).resolve().parents[1]
ANALYZER_PROJECT = REPO / 'GameFrameX.Architecture.Analyzers/GameFrameX.Architecture.Analyzers.csproj'
ANALYZER = REPO / 'bin/app_debug/GameFrameX.Architecture.Analyzers.dll'


def run(arguments, expected_diagnostic=None):
    result = subprocess.run(['dotnet', *map(str, arguments), '--nologo', '-v:q'],
                            cwd=REPO, capture_output=True, text=True,
                            env={**os.environ, 'DOTNET_CLI_UI_LANGUAGE': 'en'})
    output = result.stdout + result.stderr
    if 'CS9057' in output or 'AD0001' in output:
        raise RuntimeError('分析器未正确加载或崩溃：\n' + output)
    if expected_diagnostic:
        if result.returncode == 0 or f'error {expected_diagnostic}:' not in output:
            raise RuntimeError('未得到预期编译错误：' + expected_diagnostic + '\n' + output)
    elif result.returncode != 0:
        raise RuntimeError(output)
    return output


def project(folder, name, items='', source='public class Consumer { }'):
    folder.mkdir(parents=True)
    csproj = folder / 'Fixture.csproj'
    csproj.write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework><AssemblyName>{name}</AssemblyName>
    <PackageId>{name}</PackageId><Version>0.0.0-c241</Version>
  </PropertyGroup>
  <ItemGroup>{items}</ItemGroup>
</Project>''')
    (folder / 'Fixture.cs').write_text(source)
    return csproj


def main():
    run(['build', ANALYZER_PROJECT])
    with tempfile.TemporaryDirectory(prefix='c241-boundary-') as temporary:
        root = Path(temporary)
        feed = root / 'feed'
        feed.mkdir()
        (root / 'NuGet.Config').write_text(f'''<configuration>
  <packageSources><clear/><add key="fixture" value="{escape(str(feed))}"/></packageSources>
  <config><add key="globalPackagesFolder" value="{escape(str(root / 'packages'))}"/></config>
</configuration>''')
        implementation = project(root / 'implementation', 'GameFrameX.Capability.Online.Social',
                                 source='public class Implementation { }')
        wrapper = project(root / 'wrapper', 'C241.Adapter',
                          f'<ProjectReference Include="{escape(str(implementation))}"/>',
                          'public class Wrapper { public Implementation Value; }')
        for package in (implementation, wrapper):
            run(['pack', package, '-o', feed])
        for consumer in ('GameFrameX.Apps', 'GameFrameX.Hotfix'):
            analyzer_item = f'<Analyzer Include="{escape(str(ANALYZER))}"/>'
            for kind, dependency in (
                ('direct-project', f'<ProjectReference Include="{escape(str(implementation))}"/>'),
                ('transitive-project', f'<ProjectReference Include="{escape(str(wrapper))}"/>'),
                ('direct-package', '<PackageReference Include="GameFrameX.Capability.Online.Social" Version="0.0.0-c241"/>'),
                ('transitive-package', '<PackageReference Include="C241.Adapter" Version="0.0.0-c241"/>'),
            ):
                fixture = project(root / f'{consumer}-{kind}', consumer, dependency + analyzer_item)
                run(['build', fixture], 'GFX0017')
                print(f'PASS {consumer}: {kind} -> GFX0017 (无 using / 无实现类型使用)')
        legal = project(root / 'legal-launcher', 'GameFrameX.Launcher',
                        f'<ProjectReference Include="{escape(str(wrapper))}"/>'
                        f'<Analyzer Include="{escape(str(ANALYZER))}"/>')
        run(['build', legal])
        print('PASS Launcher: transitive implementation -> legal')
        contracts = REPO / 'bin/app_debug/GameFrameX.Capability.Online.Abstractions.dll'
        bridge_items = (f'<Reference Include="Online.Contracts"><HintPath>{escape(str(contracts))}</HintPath></Reference>'
                        f'<Analyzer Include="{escape(str(ANALYZER))}"/>')
        holders = ('OnlineFriendDomainBridgeHolder', 'OnlineMatchGameBridgeHolder', 'OnlineMatchDomainBridgeHolder')
        prefix = 'GameFrameX.Capability.Online.Abstractions.Bridges.'
        lifecycle = 'public class Wiring { public void Run() { ' + ''.join(
            prefix + holder + '.Retract();' for holder in holders) + ' } }'
        reads = 'public class Adapter { public void Run() { ' + ''.join(
            '_ = ' + prefix + holder + '.Current.IsActive;' for holder in holders) + ' } }'
        for consumer in ('GameFrameX.Apps', 'GameFrameX.Hotfix'):
            negative = project(root / f'{consumer}-bridge-publish', consumer, bridge_items, lifecycle)
            output = run(['build', negative], 'GFX0018')
            for holder in holders:
                assert holder + '.Retract()' in output, output
            print(f'PASS {consumer}: three bridge lifecycle calls -> GFX0018')
            positive = project(root / f'{consumer}-bridge-current', consumer, bridge_items, reads)
            run(['build', positive])
            print(f'PASS {consumer}: three holder.Current / lease reads -> legal')
        wiring = project(root / 'legal-wiring', 'GameFrameX.Launcher', bridge_items, lifecycle)
        run(['build', wiring])
        print('PASS Launcher: three bridge lifecycle calls -> legal')
        print('PASS 14/14 MSBuild boundary fixtures')


if __name__ == '__main__':
    main()
