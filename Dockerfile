FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# 先复制 restore 所需的工程与属性文件：仅当项目结构/版本清单变化时才重新 restore，源码变更不再使依赖层缓存失效
COPY Directory.Build.props Directory.Build.targets Version.props global.json ./
COPY Capability/GameFrameX.Capability.Hosting.Abstractions/GameFrameX.Capability.Hosting.Abstractions.csproj Capability/GameFrameX.Capability.Hosting.Abstractions/
COPY Capability/GameFrameX.Capability.Hosting.AdminApi/GameFrameX.Capability.Hosting.AdminApi.csproj Capability/GameFrameX.Capability.Hosting.AdminApi/
COPY Capability/GameFrameX.Capability.Hosting.Core/GameFrameX.Capability.Hosting.Core.csproj Capability/GameFrameX.Capability.Hosting.Core/
COPY Capability/GameFrameX.Capability.Online.Abstractions/GameFrameX.Capability.Online.Abstractions.csproj Capability/GameFrameX.Capability.Online.Abstractions/
COPY Capability/GameFrameX.Capability.Online.Assets/GameFrameX.Capability.Online.Assets.csproj Capability/GameFrameX.Capability.Online.Assets/
COPY Capability/GameFrameX.Capability.Online.Competition/GameFrameX.Capability.Online.Competition.csproj Capability/GameFrameX.Capability.Online.Competition/
COPY Capability/GameFrameX.Capability.Online.Foundation/GameFrameX.Capability.Online.Foundation.csproj Capability/GameFrameX.Capability.Online.Foundation/
COPY Capability/GameFrameX.Capability.Online.Match/GameFrameX.Capability.Online.Match.csproj Capability/GameFrameX.Capability.Online.Match/
COPY Capability/GameFrameX.Capability.Online.Matchmaking/GameFrameX.Capability.Online.Matchmaking.csproj Capability/GameFrameX.Capability.Online.Matchmaking/
COPY Capability/GameFrameX.Capability.Online.Operations/GameFrameX.Capability.Online.Operations.csproj Capability/GameFrameX.Capability.Online.Operations/
COPY Capability/GameFrameX.Capability.Online.Persistence/GameFrameX.Capability.Online.Persistence.csproj Capability/GameFrameX.Capability.Online.Persistence/
COPY Capability/GameFrameX.Capability.Online.Social/GameFrameX.Capability.Online.Social.csproj Capability/GameFrameX.Capability.Online.Social/
COPY GameFrameX.Apps/GameFrameX.Apps.csproj GameFrameX.Apps/
COPY GameFrameX.Architecture.Analyzers/GameFrameX.Architecture.Analyzers.csproj GameFrameX.Architecture.Analyzers/
COPY GameFrameX.Client/GameFrameX.Client.csproj GameFrameX.Client/
COPY GameFrameX.Config/GameFrameX.Config.csproj GameFrameX.Config/
COPY GameFrameX.Core/GameFrameX.Core.csproj GameFrameX.Core/
COPY GameFrameX.DataBase/GameFrameX.DataBase.csproj GameFrameX.DataBase/
COPY GameFrameX.DataBase.Mongo/GameFrameX.DataBase.Mongo.csproj GameFrameX.DataBase.Mongo/
COPY GameFrameX.DataBase.PostgreSql/GameFrameX.DataBase.PostgreSql.csproj GameFrameX.DataBase.PostgreSql/
COPY GameFrameX.DataBase.Sqlite/GameFrameX.DataBase.Sqlite.csproj GameFrameX.DataBase.Sqlite/
COPY GameFrameX.Discovery/GameFrameX.Discovery.csproj GameFrameX.Discovery/
COPY GameFrameX.Hotfix/GameFrameX.Hotfix.csproj GameFrameX.Hotfix/
COPY GameFrameX.Hotfix.WrapperGenerator/GameFrameX.Hotfix.WrapperGenerator.csproj GameFrameX.Hotfix.WrapperGenerator/
COPY GameFrameX.Launcher/GameFrameX.Launcher.csproj GameFrameX.Launcher/
COPY GameFrameX.Localization/GameFrameX.Localization.csproj GameFrameX.Localization/
COPY GameFrameX.Network.Abstractions/GameFrameX.Network.Abstractions.csproj GameFrameX.Network.Abstractions/
COPY GameFrameX.Network/GameFrameX.Network.csproj GameFrameX.Network/
COPY GameFrameX.Network.HTTP/GameFrameX.Network.HTTP.csproj GameFrameX.Network.HTTP/
COPY GameFrameX.Network.RemoteMessaging/GameFrameX.Network.RemoteMessaging.csproj GameFrameX.Network.RemoteMessaging/
COPY GameFrameX.Proto/GameFrameX.Proto.csproj GameFrameX.Proto/
COPY GameFrameX.ProtoBuf.Net/GameFrameX.ProtoBuf.Net.csproj GameFrameX.ProtoBuf.Net/
COPY GameFrameX.StartUp/GameFrameX.StartUp.csproj GameFrameX.StartUp/
COPY GameFrameX.Templates/GameFrameX.Templates.csproj GameFrameX.Templates/
COPY GameFrameX.Utility/GameFrameX.Utility.csproj GameFrameX.Utility/
RUN dotnet restore "GameFrameX.Launcher/GameFrameX.Launcher.csproj"
RUN dotnet restore "GameFrameX.Hotfix/GameFrameX.Hotfix.csproj"
COPY . .
WORKDIR "/src/GameFrameX.Launcher"
RUN dotnet build "GameFrameX.Launcher.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "GameFrameX.Launcher.csproj" -f net10.0 -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# 编译Hotfix程序集
WORKDIR "/src/GameFrameX.Hotfix"
RUN mkdir -p /app/build/hotfix
RUN dotnet build "GameFrameX.Hotfix.csproj" -c $BUILD_CONFIGURATION -o /app/build/hotfix /p:UseSharedCompilation=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# 复制json配置文件
COPY GameFrameX.Config/json ./json

# 复制Hotfix编译结果到发布目录（包含所有依赖）
RUN mkdir -p /app/hotfix
COPY --from=publish /app/build/hotfix/ /app/hotfix/
# 热更依赖加载逻辑默认从 /app 根目录查找引用程序集，这里同步 DLL 到根目录
RUN cp -f /app/hotfix/*.dll /app/

# 切换到root用户：创建数据目录（归属 $APP_UID，非 root 运行用户可写——SQLite 缺省文件 data/gameframex.db 落此）、写入入口脚本并赋权
USER root
RUN mkdir -p /app/data && chmod 755 /app/data && chown $APP_UID:0 /app/data

# 单一镜像多角色：镜像不固化、不检查任何业务参数。
# 启动参数由 GAMEFRAMEX_SERVER_ARGS 环境变量原样透传（含 --ServerType / --DataBaseUrl 等完整参数），
# 未设置时不传参数，由应用默认逻辑启动首个可用服务器。
COPY <<'ENTRYPOINT' /app/entrypoint.sh
#!/bin/sh
exec dotnet GameFrameX.Launcher.dll ${GAMEFRAMEX_SERVER_ARGS:-} "$@"
ENTRYPOINT
RUN chmod +x /app/entrypoint.sh

# 声明数据卷以实现数据持久化
VOLUME ["/app/data"]

# 切换回非root用户运行应用
USER $APP_UID

ENTRYPOINT ["/app/entrypoint.sh"]
