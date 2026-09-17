FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY . .
RUN dotnet restore "GameFrameX.Launcher/GameFrameX.Launcher.csproj"
RUN dotnet restore "GameFrameX.Hotfix/GameFrameX.Hotfix.csproj"
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
RUN cp -f /app/hotfix/*.dll /app/ || true

# 切换到root用户：创建数据目录、写入入口脚本并赋权
USER root
RUN mkdir -p /app/data && chmod 755 /app/data

# 复制发布文件
COPY --from=publish /app/publish .

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
