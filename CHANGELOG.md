## [Unreleased] - PostgreSQL-Only 验证分支（verify/pg-only-no-mongo）

### Removed

* 完全移除 MongoDB 依赖：删除 GameFrameX.DataBase.Mongo 模块、MongoDbService/MongoDiscoveryRuntime 装配分支、BSON 类映射辅助（BsonClassMapHelper/CacheStateTypeManager）与 Mongo 专属测试
* BaseCacheState.ToBytes 由抽象改为 JSON/UTF8 默认实现（StateHash 脏检查不再依赖 BSON）
* 业务状态类基类由 Mongo CacheState 切换为 BaseCacheState；移除 Apps/Hotfix/Launcher/StartUp 的全部 MongoDB using 与包引用
* DatabaseProviderType 枚举与 DatabaseProvider 配置项移除（PostgreSQL 为唯一实现）
* StartUp 服务默认配置移除 MongoDB 健康检查与 Mongo Meter 注册；清理 Mongo 相关本地化键

### Known Issues

* GameFrameX.Foundation.Logger ≥ 4.1.1 的 nuspec 无条件声明 MongoDB.Driver 与 Serilog.Sinks.MongoDB（运行时 IsWriteToMongoDb 默认关闭）。已解除——Foundation 5.0.0 将 Mongo/Loki sink 拆为可选组件包，随 C224 全量升级后传递依赖归零，验证 feed 补丁包不再需要

## [Unreleased]

### Added
* 服务器间 RPC 端点改由心跳注册表解析并退役静态引导（C304，GitHub #643）：`RemoteServiceTarget`（ServerType + 可选 InstanceId）结构化拨号取代 `{Role}_{InstanceId}` 字符串拼接；`DiscoveryServiceEndpointResolver` 成为唯一端点解析器（指定实例走 Instance 视图含 Draining 并校验 Role 一致，ServerType 级取 Active 中 InstanceId 序数最小者，端点集合 = TCP 主端点 + 附加端点，偏好 kcp > ws > wss > tcp）；`RemoteMessagingBuilder.WithRouteTable(IRoleRouteTableProvider)` 绑定路由表、生产注册 KCP 拨号适配器（`IsEnableInnerKcp` 时，服务器间 KCP 首次生产可用）；心跳新增附加广播端点（`InstanceDescriptor.AdditionalAdvertiseEndpoints`，监听器装配成功的实例级闸门后才广播，多 ServerType 进程非首选类型披露 Warning）；`UnifiedMessageSender` 实例列表改读路由表 Active 视图且仅 InstanceId 集合变化才重建哈希环；玩家路由实例直达——`player_route.InstanceId` 写真实发现层实例 Id（`PlayerRouteResolverBootstrap` 同源注入），`ForwardToRemoteServerAsync` 按 `TargetInstanceId` 投递（Instance 视图含 Draining）、`IsCurrentServer` 改 InstanceId 比较；Mongo 心跳 class map 显式忽略未知字段（含继承传递）、写路径空集合归一化为 null（默认配置文档与旧版本逐字节同形）；PostgreSQL 幂等补列 `additional_advertise_endpoints text[]`（命名经 `DiscoveryStorageNaming` 推导）；删除 `services__*` 环境变量契约、`EnvironmentVariableEndpointResolver`、`EnvironmentServiceInstanceDiscovery`、`docker-compose.multi.legacy.yml` 与生成器静态引导映射。
* `RemoteMessagingBuilder.WithConnectionProvider(Func<IConnectionProvider>)` 连接提供器注入点（C354，GFX-1062 / GitHub #690）：测试可注入替身工厂替代缺省的进程级共享 TCP 连接池；生产路径不调用时行为零变化。配套 `TcpConnectionProvider` 新增 internal 建连延迟参数（放大建连窗口，使并发排队可确定性复现）。


* Capability 架构护栏（C241）：GFX0017 禁止 Apps/Hotfix 的直接及传递编译引用依赖实现包（保留 Online.Abstractions 及其合法 Hosting 契约闭包）；GFX0018 以符号检查能力注册、builder 装配与三族桥 Publish/Retract 的所属边界，保留模块辅助注册、Launcher Wiring 与独立测试夹具。Capability 工程统一接入分析器；架构分析器 Roslyn 引用对齐 .NET 10 SDK 基线，防止 CS9057 跳过规则；Online.Persistence 针对 C249 域属行状态仅本工程豁免旧 GFX0001。静态检查不证明反射、动态加载、跨请求缓存或启停时序。
* Online 存储模式无声降级防护（C302，GFX-1015）：`Configs/app_config.json` Game Role 的 Online 段补 `UseGameDbStores: true`（原先缺省走 InMemory，重启丢玩家身份/好友/资产/存储数据）；启用 Online 能力族（双闸任一开）但未显式声明存储模式时组合根发出启动期 Warning（新本地化键 `Capability.Online.UseGameDbStoresUndeclared`，判定收敛为纯谓词 `OnlineCapabilityCompositionRoot.ShouldWarnStoreModeUndeclared` 供单测断言）；不 fail-fast，保护既有无键部署。
* 两层会话协同与真实断线恢复（C204）：Core 物理连接 / Online 逻辑会话 / Online 对局成员三层职责与标识映射冻结——身份恢复凭据 = Online 会话 Token（`RespPlayerLogin` 新增 `SessionToken` 字段断线前交付；桥不可用为空、登录主流程不挂），新连接携凭据经新增 220-18/19 `ReqSessionResume`/`RespSessionResume`（Auth 族）恢复：验证（指纹/终态/到期/作用域）→ 连接替换（`OnlineSessionManager.MarkResumedAsync` 新面，不放松既有状态机）→ 原子轮换（旧凭据即刻失效，重放显式 TokenRevoked）→ Core 绑定（`UpdateSession` 顶号编排复用）→ 在线簿记还原 → 对局恢复（`IOnlineMatchDomain` 增 `NotifyPlayerDisconnectedAsync`/`TryResumeForPlayerAsync`——按票据→分配登记→运行时成员表逐级核对，服务端留存 rt- 令牌回灌 `ResumeAsync` 三重校验原样生效，快照+增量/回退，不重新匹配、不另开对局、不重复结算；无对局 = 空回执、超窗 NetworkTimeout、已结束 StateEnded、已离局 ScopeDenied）。`Capability.Online.Abstractions` 新增 `IOnlineSessionReconnectContract` + `OnlineSessionDomainBridgeHolder/Lease`（C246 匹配域桥同型）；Foundation `OnlineSessionReconnectService` 实现（不另建第二凭据权威——Token 签发/校验/轮换全部委托既有 Token 服务）+ `session-reconnect-window-sweep` 清扫 tick；`PlayerSessionManager.Remove` 带路由下线守卫（旧断连回调迟到不清新路由）；`OnDisconnected` 接入两层协同（会话域开窗 + 对局域标记断线成员，旧回调迟到守卫跳过）；KCP 首消息白名单补 `ReqSessionResume`（与角色登录同为鉴权完成信号）；Bot `--scenario=match` 扩展断线恢复演练（断线→携凭据恢复同身份同对局→旧凭据重放被拒→幂等重入同一局证据链）。宿主重启 = InMemory 会话/票据/对局失忆，恢复承诺终止于进程存活期（不宣称跨重启无损恢复）。


* 匹配域接管——即时/异步撮合语义收敛与旧 FIFO 链退役（C246，D2 Match）：匹配池唯一权威收敛为 Capability 票据撮合域（`OnlineMatchTicketService` + `OnlineMatchmakerCoordinator` 同一票据存储），域 420 三入口（入队/取消/状态）全部改经新域契约面 `IOnlineMatchDomain`（Enqueue/Cancel/GetStatus/Enter/ResolveAssignment + DTO 族）+ `OnlineMatchDomainBridgeHolder/Lease` 进程内吊销式定位；即时窗口兼容——入队建票后同步跑一轮撮合（同一票据存储=同一池权威，tick 互为冗余兜底），对手已在池时响应即刻带数值对局标识（旧 FIFO 链第二人入队即配对可观测行为保持），等待方经状态轮询/通知获知；语义映射——Mode(1/2) 原值入票为池分区键且 TeamSize 恒 2（旧链无论 1v1/2v2 恒配 2 人）、Rating→SkillRange ±100 基础窗（旧基础窗同值，等待扩展节奏近似披露）、取消终态回执映射旧 420003、票据 TTL 300s 超时为产品新增语义、错误码本地校验 420004/420005+查重 420002+终态 420003 而其余走 `-(int)OnlineErrorCode` 显式段；新增 additive 入场对 `ReqMatchEnter`/`RespMatchEnter`（420-16/17，数值 id→运行时对局 `matched-s{区服}-m{数值}` 确定性派生 create-or-reuse，双方各自到达同一运行态对局）与单向通知 `NotifyMatchMade`（420-18，Launcher 组合根订阅 `Online.Match.AssignmentCreated` 事件→域服务解析数值标识→双方在线会话 best-effort 推送，离线经状态轮询兜底）；数值对局标识登记表（assignment→数值 id 单射，InMemory 与票据存储同生命周期）；`IOnlineMatchTicketStore` 增 `FindLatestByPlayerAsync`（任意态最新票读，成组提交释放玩家反查索引故按票据表扫描）；Match 模块 manifest 依赖追加 `online-matchmaking`；Hotfix `MatchDomainBridge` 静态薄适配器 + 四处理器改 plain `BaseRpcMessageHandler`；HTTP 验证入口 `mode=online-bridge-matchdomain&action=enqueue|cancel|status|enter`。**Removed（破坏性）**：旧 FIFO 内存池链整体退役——`MatchComponentAgent`/`MatchRules`/`MatchComponent`/`MatchState` 实体族与 Match Role 进程（`AppStartUpMatch`/`GameServerConst.Match`/配置 Match 段）删除，进程 Role 集 18→17、README×5 计数同步；旧 GameDb MatchState 文档零改动（孤儿数据披露）。
* 好友域接管——120/660 全族迁移 Online 好友域与旧链退役（C244，D2 Friend）：好友域唯一写者收敛为 Capability `OnlineFriendService`，域 120（直接添加/删除/列表）与域 660（申请/接受/拒绝/解除/全量列表）协议入口全部改经新域契约面 `IOnlineFriendDomain`（ListFriends/ListRelations/Request/Accept/Reject/Remove/Establish + 读模型 OnlineFriendRelationKind/OnlineFriendRelation/OnlineFriendRelationChange；定位一律玩家对）+ `OnlineFriendDomainBridgeHolder/Lease` 进程内吊销式定位转发；Hotfix 侧 8 个处理器改 plain `BaseRpcMessageHandler` 薄适配（无组件无 ActorId），120 族请求主体从缺陷语义（玩家型组件 ActorId 每调用新生成=恒空列表）修复为会话绑定玩家 id（未登录显式 `-(int)SessionInvalid`），120 直接添加映射 Establish（申请+立即接受，对方先申请时等价本人接受），错误码 120 保留 -100/-101/-103/-104（-102 废弃披露）、660 全保留 660001..660005；好友持久化 `GameDbOnlineFriendStore` 随 `UseOnlineGameDbStores` 预置（SaveIfAbsent 冲突读回既有绝不覆盖）；旧数据一次性迁移 `OnlineFriendLegacyMigration`（开关 `Online.MigrateLegacyFriendData`，只读单向复制旧 FriendRelationState/660 FriendState、幂等、旧数据零改动）；**Removed（破坏性）**：C243 查询缝四件（IOnlineFriendQuery/OnlineFriendQueryFace/OnlineFriendBridgeHolder+Lease/FriendQueryBridge）、开关 `EnableFriendQueryBridge` 与种子键 `FriendQueryBridgeSeedPlayerIds`（种子沿用改名 `FriendBridgeSeedPlayerIds`）、旧 120 Inner 写链（Player FriendComponentAgent + 三个 ReqInnerFriend* handler + FriendRelationState 写路径）、旧 660 状态机（ServerRole FriendComponentAgent/FriendRules/FriendComponent/FriendState 组件）、Friend Role 进程入口（AppStartUpFriend/GameServerConst.Friend/配置 Friend 段，进程 Role 集 19→18）——好友消息此后仅 Game 型进程可服务（宿主在 Game 进程），`IsEnableOnline=false` 部署显式失败不回落；HTTP 验证入口增写路径族 `mode=online-bridge-friend&action=request|accept|reject|remove|relations&peer=`。

* 好友查询桥接缝——首个 Hotfix→Capability 桥接闭环（C243，承接 C240 声明）：`Capability.Online.Abstractions` 新增 `IOnlineFriendQuery` 契约面 + `OnlineFriendEntry` 最小读模型 + `OnlineFriendBridgeHolder/OnlineFriendBridgeLease` 进程内吊销式定位（lease 逐调用复查活跃位，撤销/替换后旧引用一律显式 `DependencyUnavailable(8002)` 失败，不回旧数据）；Social 模块以接口键注册薄映射实现 `OnlineFriendQueryFace`；Hotfix `FriendQueryBridge` 薄适配器（`GameFrameX.Hotfix` 新增对 `Capability.Online.Abstractions` 的引用，D2 裁定方向）在 `Online.EnableFriendQueryBridge`（"Online" 嵌套段，默认 false=旧链行为逐字节保持）开启时独占接管 `ReqFriendList`——作用域三元组只取 lease 携带的组合根授权值、玩家位只取 ActorId，桥失败映射 `-(int)OnlineErrorCode`（≤ -1001 段，与旧链 -1..-99 不重叠），绝不静默回落或空列表；Game 组合根 `OnlineFriendBridgeWiring` 装配前拓扑 fail-fast（开关开而 `IsEnableOnline` 关/作用域缺失/契约面被过滤器排除即启动终止）、启动后发布租约与可选验证种子（`Online.FriendQueryBridgeSeedPlayerIds` 首元素=请求方，经真实 `OnlineFriendService` 写路径幂等建立，Warning 披露）、退出先撤销；Hotfix HTTP 验证入口增 `mode=online-bridge-friend&playerId=`（与协议入口同一适配器代码路径）；AllInOne 与 Game/Social 分进程真实 HTTP/TCP 入口实证预置非空好友读取、TCP 入口零旧链内部消息、开关关闭面显式 false（详见 online-platform spec「好友查询桥接缝（C243）」）。

* 双玩家对局、结算、发奖与排行生产闭环（C245，D4 RPS 垂直切片）：`Capability.Online.Abstractions` 新增 `IOnlineMatchGameBridge` 契约面（StartRoomGame / SubmitGesture / GetGameInfo → 保密投影视图 `OnlineMatchBridgeGameView` + `OnlineMatchBridgeGameState` 生命周期投影）与 `OnlineMatchGameBridgeHolder/Lease` 进程内吊销式定位（C243 holder/lease 同型，撤销后旧引用显式 `DependencyUnavailable(8002)`）；Match 包 `OnlineMatchGameBridgeService` 桥实现——对局标识确定性派生 `rps-s{serverId}-r{roomId}-g{round}`（同房间同局号幂等 create-or-reuse，重开 = 新局号新对局、旧局终态不复活）、`ClientSequence` 服务端从快照派生（请求体不可携带）、Ack 拒绝段化错误码映射、出拳保密投影由玩法插件 `OnlineRockPaperScissorsGame.BuildBridgeView` 承载（当前局 Gesture 一律 0、已结算局回显真实出拳、Wins 不外发——现网 410 协议无比分字段的样例边界）；Match 模块生产装配结算编排 `OnlineMatchSettlementService` + `OnlineMatchResultDispatcher`（assets 能力缺席时 dispatcher 可空 = 仅结算不发奖，season/tournament 模块化等价先例）+ `OnlineMatchSettlementPipeline`（tick job `match-settlement-tick`：Settling 态 → 幂等结算（result store 首结果获胜）→ 非回放结果逐玩家幂等发奖（`{MatchResultId}-{PlayerId}` 业务单号）→ 失败进待重试表逐 tick 重投 + admin 可观测面 `query_match_settlement`，Tick 互斥）；Competition 模块订阅 `Online.Match.Settled`（Operations 事件桥先例）经既有 `OnlineLeaderboardResultProjector` 可信结果写榜（载荷零信任：result store 直读 + MatchResultId 核对；幂等键 `lb-{MatchResultId}-{LeaderboardId}-{PlayerId}`；失败待重试 tick `leaderboard-projection-retry`；manifest 依赖追加 online-match）+ 查询 action `query_leaderboard`；Hotfix `RockPaperScissorsMatchBridge` 薄适配器接线四入口（开关 `Online.EnableMatchGameBridge` 默认 false = 旧链逐字节保持；开启时 Room 开局桥先行（失败显式负值不推进 Room，确定性局号使重发自愈）、出拳/查询/重开桥独占、房间状态映射 Settling→EnterSettling / 终态→MarkSettled、全员保密推送；错误码 `-(int)OnlineErrorCode` 段显式失败绝不回落）+ HTTP 验证入口 `mode=online-bridge-match`；Launcher `OnlineMatchBridgeWiring` 组合根接线（fail-fast / 启动后发布 / `Online.MatchBridgeLeaderboardId` 非空时预置投影目标并经真实榜单服务幂等确保存在（Warning 披露）/ 停机双桥撤销先于 admin 与 runtime 停止）。不宣称全局恰好一次：结算/发奖/写榜幂等边界分置且各自失败可观测可恢复；对局/结果/榜单 store 与幂等协调器仍为 InMemory（D3 现状，重启易失边界披露）；旧 RPS 本地执行（Apps 单局制）不退役——平台三局两胜制为非等价替换，T7 裁定零删除（生产验证后另立退役 change）。

* 网络面纵深防御五修（C272，GFX-912）：①Admin 面限流——`AdminApiServer` 接入 ASP.NET Core 按 IP 固定窗口限流（`AdminApiOptions.RateLimitPermitLimit=100` / `RateLimitWindowSeconds=10`，<=0 显式关闭，超限 429），登录爆破/admin 重放/消息洪泛得到闸门；②节点间 RPC 鉴权——协议版本握手帧（C252）承载 HMAC-SHA256 共享密钥凭据（`ReqProtocolVersionNegotiation` 增 `Nonce`/`AuthCredential` 字段、`Resp` 增 ErrorCode=2 鉴权失败语义、`InterNodeAuthentication` 进程级配置 + 固定时间比较、`AppSetting.InterNodeAuthSecret` 空=不启用零破坏），服务端验签失败回拒绝包并关闭连接、客户端握手超时不再回落 Legacy（fail-closed 防降级面）、InnerKcp 专用监听在密钥配置时挂凭据闸门（未过握手前白名单外消息按协议违规关闭）；③会话凭据校验缝——`SessionAuthenticationOptions.CredentialValidator`（AuthenticatedBy 消息命中先过凭据校验，false=协议违规关闭，null=旧行为）；④protobuf 读侧深度上限——`ProtoReader.MaxDepth=100`（`StartSubItem` 越限抛 `ProtoException`，深嵌套恶意载荷由 StackOverflow 打崩进程转为快速失败，对齐主流 protobuf 实现递归上限）；⑤调试日志脱敏——`MessageLogRedaction`（默认敏感词表 password/token/secret 大小写不敏感包含匹配 + JsonNode DOM 遮蔽为 `***`，控制台与 JSON 两条输出路径，词表可扩展），收发日志不再泄露 Password/Token；README×5 配置表与 inter-server KCP 段落同步。

* Network.HTTP 安全面三修（C271，GFX-911）：①HTTP 签名从可伪造方案（`md5("38848"+timestamp)` + 公开常量校验码 88/66，零秘密、任何源码读者可对任意 timestamp 伪造合法签名）换为 `sign = Base64(HMAC-SHA256(UTF8(timestamp), UTF8(HttpSignSecret)))`（固定时间比较）；新增 `HttpSignSecret` 配置（ProcessLevel），开启校验（`HttpRequireSign` 或 `[RequireHttpSignature]`）而密钥空白时 fail-closed 一律 401 + Error 日志（新本地化键 `Network.Http.SignSecretMissing`）；删除 `BaseHttpHandler.CheckCodeTime` / `CheckCodeStart` / `CheckCodeEnd` 可伪造常量属性（对外破坏性：旧签名客户端须按新口径重算，仓内零消费者）。②`HttpHandler.HandleRequest` 顶层异常信封不再回传 `e.Message`（内部路径/连接串片段泄露面），客户端只回通用文案 `Network.Http.ServerError`，完整异常仍落服务端日志。③HTTPS 真实现：`HttpsPort > 0` 从抛 `NotImplementedException` 改为 Kestrel `ListenAnyIP(port, lo => lo.UseHttps(cert))` 真监听；新增 `HttpsCertPath`（PFX 路径）/ `HttpsCertPassword` 配置（ProcessLevel），配置期 fail-fast 校验（路径空/文件缺失/加载失败三本地化异常，`StartUp.HttpExceptions.HttpsCert*`；`HttpsNotImplemented` 键随分支删除）；移除端口被占用静默跳过分支（HTTPS 被要求时 bind 失败即启动失败，消除静默 TLS 降级面）；README×5 网络配置表同步。
* 热更版本切换运维入口唯一化（C256，用户裁定「入口唯一」）：Online admin 面 hotfix 受控操作族四 action 落地（`OnlineAdminHotfixHandlers`：register_hotfix_manifest / activate_hotfix_version / check_hotfix_compatibility / rollback_hotfix_version，直接映射 `OnlineHotfixRollbackService` 服务面；作用域两键从 admin scope 锚定、body 不携带作用域字段；dispatcher 幂等包裹 false——服务自幂等，grant 族先例；`OnlineOperationsCapabilityModule` 的 `OnlineHotfixRollbackService` 提为 `_hotfixRollback` 字段并接线 `RegisterAdminActions`）；旧 HTTP 通道 `ReloadHttpHandler`（`POST /game/api/Reload`，吞 bool 装载失败仍回成功，C248 R8 留档缺陷）随类退役删除，README×5 热更 API 段改写为 admin 通道说明（admin 面暂无 HTTP 签名校验，依赖部署侧端口隔离 28090，R-07 待做）——运行期热更版本切换经全仓仅剩 Online admin 受控通道一个触发面；`HotfixManagerRollbackExecutor.RecoverOriginalAsync` 恢复路径改传 `_setting` 与主路径口径统一（C248 review P3 #1 关闭）。

* Hotfix 版本切换执行器与登记期制品分发生产实现（C248，vault R-01 / D-3 = A 补实现，向前+向后统一）：`GameFrameX.Launcher/Online/` 新增 `HotfixManagerRollbackExecutor : IOnlineHotfixRollbackExecutor`——方向无关切换 = 单次 `HotfixManager.LoadHotfixModule(setting, {Version=target})`（内部同路径幂等守卫 → 旧模块 Retire 10 分钟退役宽限（in-flight 按程序集身份路由，C216）→ 指针切换 → 桥接 OnLoadSuccess）+ 在盘预检（切换时刻零下载红线：目标未落盘即拒，静态无网络 IO）+ 装载路径健康校验 + 失败按切换前装载路径自动恢复原版本 + `SemaphoreSlim(1,1)` 进程内互斥；组合根 `OnlineCapabilityComposition.BuildScannedRuntime` 经既有缝 `UseOnlineHotfixRollbackExecutor` 注入——生产路径 8002 从「执行器恒缺席」改为真失败。登记期制品分发：`OnlineHotfixProtocolManifest` 扩可选字段 `ArtifactSourceUrl` / `ArtifactSha256`（成对提供 = 登记时下载；皆缺 = 纯本地登记行为逐字节不变；sha-only = 校验在盘制品一致性）；Operations 包新增 `OnlineHotfixArtifactFetcher`（BCL HttpClient + SHA256，不引 Core）——下载至同卷 staging → 校验 → 原子 rename 至 `{CWD}/{Version}/GameFrameX.Hotfix.dll`，失败零残留可重试、版本目录一经落盘不可变更，`OnlineHotfixArtifactFetchOptions` 定值（单次尝试 30s / 重试 2 次仅网络异常与 5xx/408 / 上限 64MiB）；服务 `RegisterAsync` 获取器先行（形状非法 4001 / 获取失败或未装配 8002 登记未生效），新缝 `UseOnlineHotfixArtifactFetcher` 同步组合根注入；版本号新增与 `HotfixLoadOptions` 同源路径注入防线（拒 `/` `\` `:` 与 `..`）。

* Online store 分级持久化（C249，D3 = 3A）：新工程 `Capability/GameFrameX.Capability.Online.Persistence`——一级 store（Identity / PlayerStorage / 资产账本 + 交易）的 GameDb 适配四件（`GameDbOnlineIdentityStore` / `GameDbOnlinePlayerStorageStore` / `GameDbOnlineAssetStore` / `GameDbOnlineAssetTransactionStore`，消费方接口零改动）+ 八个落库 state（物化复合键 UniqueKey 单列唯一索引承接唯一性；行 Id 无自然主键时 `NextSequenceAsync` 分配，账号/玩家标识与玩家账本序均由数据库键控序列接管——重启不复用）；CAS 经 C247 `UpdateIfRowVersionAsync`（Version ↔ RowVersion 恒等式 = 实体 Version = 行 RowVersion + 1，CAS 目标 = expectedVersion − 1）、仅创建经 `TryAddUniqueAsync`；软删可见性以 `DeletedAtTime` 业务字段为单一事实源，适配层零物理删除（storage 行长存 = 复活契约、账本只追加），资产落账 = 账本先行→快照随后（崩溃残留由既有对账服务以账本重算兜底）；Storage 集合列举 = 等值下推 + 客户端 ordinal 过滤/排序（与 InMemory 逐字节一致）、账本分页 = 数值序 provider 原生。Session / Presence 保持 InMemory（易失语义固化 + Redis 演进位）。装配：`"Online"` 配置段 `UseGameDbStores`（默认 false = InMemory 零变化）→ `AddOnlineCapabilities` 组合根经 `UseOnlineGameDbStores(builder)` 在模块 Register 前预置四 store 进共享服务面；`--AllInOne --DatabaseProvider=Sqlite` 零依赖起跑不变（真进程双形态冒烟：默认关/开启均 0 异常）。门禁：包引用图与 Launcher 直引清单增 Persistence 节点；新增重启持久化验收测试 8 例（三域写→关库重开+新适配实例→读回全量一致 + 序列/账本序续用，SQLite 文件模式）

* DB 弹性收敛为共享弹性引擎（C253）：新增 `GameFrameX.DataBase/Abstractions/DbResilienceExecutor`——降级读前置检查、写门控（入口 + 每次尝试复检）、指数延迟 + jitter 重试核心环、三指标（`db_operation_retry_total` / `db_operation_fail_total` / `db_operation_latency_ms`）与最终失败包装的单一事实源；契约面 = `IDbResilienceHost`（可用性状态参数化，Mongo/PG 私有嵌套适配器零公共 API 增长，Abstractions 另供恒 Healthy 宿主）+ `IDbOperationExceptionClassifier`（语句级 `IsRetryable` / 事务级 `ShouldRetryTransaction*` / `GetFailureReason`）+ `DbResilienceExecutorOptions`（文案工厂与测试缝注入）。Mongo/PG `*.Retry.cs` 收敛为分类器（`MongoDbExceptionClassifier` / `PostgreSqlDbExceptionClassifier`，`TransientSqlStates` 随迁）+ 薄包装接线，行为等价（指标仪表名/tag 逐字节不变，provider 维度由既有 Meter 名承载，不新增 tag）；SQLite 全套弹性首次生效：`SqliteDbExceptionClassifier`（busy=5/locked=6 + timeout）+ `SqliteDbService.Retry.cs` + 21 处数据操作点接入（operationName 与幂等旗标对齐 Mongo/PG；事务壳保持单次直通，嵌套壳语义下盲目重试不安全）。契约矩阵 `DbResilienceExecutorContractTests`：同一延迟序列/异常注入下三家重试次数、退避序列、降级读触发、写门控拒绝、三指标计数逐项一致（假时钟 + 脚本化宿主，无外部依赖）

* SQLite 嵌入式零依赖起步 Provider（C222）：新工程 GameFrameX.DataBase.Sqlite（手写 Microsoft.Data.Sqlite，无 EF；每状态类型一张 (id INTEGER PRIMARY KEY, doc TEXT)，id 等值提取下推 + 剩余 filter 客户端求值，软删/时间戳语义对齐 Mongo/PG）；DatabaseProviderType.Sqlite + 双 resolver 映射；GameFrameX.Discovery 增 InProcDiscoveryRuntime（单进程自指路由表，心跳 no-op）；启动警告制（四条硬边界 + 风险自担，进程内仅一次）+ 数据文件全路径信息行 + 路径可写校验 fail-fast；启动 banner 携带 provider=Sqlite；两仓 quick_start-game.bat 改零依赖命令并清除 Atlas 凭据；Dockerfile /app/data 归属 $APP_UID。定位：快速入门 / 开发评估 / 单机小规模（单进程拓扑），一条命令起服：`--AllInOne --DatabaseProvider=Sqlite --DataBaseUrl="Data Source=data/gameframex.db"`

* Online 能力组合单入口与 "Online" 配置段（C229 / C227 P2a）：新增 `AddOnlineCapabilities(Action<OnlineCapabilityCompositionOptions>)` 单入口（GameFrameX.Online.Runtime 过渡壳静态入口，P4 随壳退役换各包 AddXxx）——作用域三元组/调度间隔/双闸（EnableRuntime/EnableAdminApi）/管理面监听/能力过滤器统一经回调承载，一次调用产出业务运行时与管理面双产物；admin-only 形态接入同一配置段（排除/白名单外条目在目录报告如实反映，业务 action 仍一律 4003）；`AppSetting.Online` 嵌套强类型配置段（OnlineCapabilityOptions POCO：Capabilities 白名单/Disable 排除表，null 或空=全装配红线；未知 Id 与白名单闭包缺口组合期 fail-fast）经既有 json 反序列化通道捕获；CLI 平铺别名 `--OnlineCapabilities` / `--OnlineDisable`（逗号列表，GetServerTypes 同款投影，仅无配置段 Role 回落档生效）；Launcher 双闸迁移到单入口（四组合行为与 C228 交付态逐项等价，启停顺序与边界日志语句级保持）；P2 期入口内部仍显式链式装配（过渡模块有参构造，反射扫描发现不了），模块无参化后同一入口自然升级为真扫描（P4 终态）

* P3 叶子能力包三包落地（C231 / C227 P3）：新工程 `GameFrameX.Capability.Online.{Assets,Social,Matchmaking}`（Matchmaking 含 Party 域，收纳 `Capability/` 分组目录）——域实现 git mv 迁包（Assets 34 文件 / Social 67+ChatAudit 投影 2 / Matchmaking 29+Party 13，命名空间改挂 `GameFrameX.Capability.Online.*`）+ 各包自装配模块（无参构造，`CapabilityAssembly` 扫描可发现，P4 前置达成）+ hosting 扩展 `AddOnlineXxxRuntime(builder, options => ...)`（回调式，能力族全局约定）与 `UseOnlineXxxStore` 存储替换缝族（Assets 2 / Social 6 / Matchmaking+Party 3，未命中构造 InMemory 默认）；admin handlers 随包（online-assets 5 action / online-social 8 action 含 match_isolation / online-matchmaking 4 action）；单体 13→9 肢体目录，壳 interim 模块收缩至 P4 域（foundation/match/competition/operations）
* 能力服务共享协议（C231）：`CapabilityRegistrationContext` 扩面服务注册/解析（`AddService<T>` / `GetService<T>` / `TryGetService<T>`，宿主级共享注册表，同类型重复注册 fail-fast）+ `CapabilityHostBuilder.UseService<T>`（组合根预置面：平台服务 / 包级 options / store 替换缝共用）+ `CapabilityHost.Services`（装配产物取用面——「壳取用叶子包装配产物」）+ `CapabilityHost.Modules`（启用模块拓扑序实例表，admin 组装单实例化——修复过渡期 CreateAll 双实例化）；模块间跨域依赖经共享面解析（拓扑序保证先序产物在场），god-host 叶子域装配语句随包走
* 能力脊柱 admin 线缆契约与审计读契约上移（C231，C223 gate 上移同款手法）：`OnlineAdminApiContract` / `OnlineAdminApiAdapters`（自 Runtime 壳）与 `OnlineAuditQuery` / `OnlineAuditRecord` / `OnlineAuditPage` / `OnlineAuditDomain` + 新窄接口 `IOnlineAuditReader`（自 Audit 域）迁入 `Capability.Online.Abstractions/Contracts`；动因 = handlers 随包的编译期前置（叶子包不引聚合域/不互引；Abstractions 增 Hosting.AdminApi 引用边，机制层低于脊柱方向合法）

* P4 上层簇与聚合三包落地（C232 / C227 P4）：新工程 `GameFrameX.Capability.Online.{Match,Competition,Operations}`——Match 对局域单域平面（end_abnormal_match + match-tick job）；Competition = Leaderboard/Season/Tournament/LiveOps 四域簇（LiveOps 自 Runtime 壳迁入，7 action）；Operations = Audit/HotfixRollback/GameEvents/Timeline/Overview 聚合五域 + Console 3 action + 事件桥/审计桥装配期接线进模块 `Register`（桥体逐语句保留，订阅时点装配期提前已披露为行为等价，C105 审计桥语义不变）；域实现 git mv 迁包 + 各包自装配模块（无参构造）与 hosting 扩展 `AddOnlineXxxRuntime`；`OnlineFoundationCapabilityModule` 自 Runtime 壳过渡模块无参化迁入 Foundation 包（三 store 替换缝探测 + 四域服务注册回共享面 + 两清扫滴答 job；suspend_resume_player handler 更名 `OnlineAdminFoundationHandlers` 随包——壳类名 Match 域名系历史误植，实为在线状态阻断位）

* Launcher 真扫描组合根（C232 / C227 P4 终态）：`AddOnlineCapabilities` 编排随壳退役迁入 `GameFrameX.Launcher.Online`（入口签名 / "Online" 配置段 / CLI 别名零变化）——`BuildScannedRuntime` 组合根基建预置（授权作用域 / 事件总线 / 事件出口 / 幂等 / 统一审计具型+读面）+ `CapabilityAssembly.DiscoverModules` 部署目录命名约定扫描（`AppContext.BaseDirectory` 下 `GameFrameX.Capability.*.dll`；反射装配的组合根 AssemblyRef 表只记代码实际引用的类型、靠不住，新增能力 = 部署产物落地即被发现，宿主零改动）+ admin-only 目录改 `DiscoverModules` 驱动（手工 `Catalog()` 退役）；装配序由显式链 Add 序换扫描发现序（competition/match 互换，拓扑性质不变）；门禁终态 = 11 包精确引用表 + `Launcher_ReferencesOnlineCapabilityPackages_Directly` 直引 8 包断言（替换 OnlyThroughRuntimeShell）+ Hosting=0 红线保持 + topic 冻结面保留（目录依赖矩阵退役，包级断言由 CapabilityPackageStructureTests 全量接管）
* Online 能力包族拆包总览（C227 umbrella 收口，2026-10-17；P1~P4 = C228 / C229+C230 / C231 / C232，C235 命名空间对齐补遗）：`GameFrameX.Online` 单体与 `GameFrameX.Online.Runtime` 过渡壳退役，能力全量收纳 `Capability/` 12 包（同版本族 Version.props 单源，全部注册 slnx）：

  | 包 | 分层 | 域与 admin action |
  |---|---|---|
  | Capability.Hosting.{Abstractions,Core,AdminApi} | 机制 | manifest/模块契约/三类 job 调度、Builder+Assembly 扫描+闭包校验、Admin 管道（去域化，对能力包引用恒零） |
  | Capability.Online.Abstractions | 脊柱 | Contracts/Events/Scope/Idempotency/Tokens + admin 线缆契约与审计读契约 |
  | Capability.Online.Foundation | 地基 | Identity/Session/Presence/Storage 四域 + 无参自装配模块（suspend_resume_player） |
  | Capability.Online.Assets | 叶子 | 钱包/库存/账本/发放（5 action） |
  | Capability.Online.Social | 叶子 | 好友/群组/聊天/通知/管制（8 action 含 match_isolation） |
  | Capability.Online.Matchmaking | 叶子 | 匹配 + Party 域（4 action） |
  | Capability.Online.Match | 上层 | 对局域（end_abnormal_match + match-tick job） |
  | Capability.Online.Competition | 上层 | Leaderboard/Season/Tournament/LiveOps（7 action） |
  | Capability.Online.Operations | 聚合 | Audit/HotfixRollback/GameEvents/Timeline/Overview + 事件桥/审计桥（Console 3 action） |
  | Capability.Online.Persistence | 持久化 | C249 增设：一级 store GameDb 适配（Identity/PlayerStorage/资产账本+交易），二级易失 store 保持 InMemory |

  P4 终态 admin 面 = 29 业务 action + list_capabilities 元端点（分布 5/8/4/1/7/3+foundation1；C256 起 hotfix 受控族 +4）；组合根双轨 = Launcher 真扫描（`AddOnlineCapabilities` 直引 9 Online 包 + Hosting 三件）与 dotnet new `gfx-online` 模板最小装配；门禁 = `CapabilityPackageStructureTests` 12 包精确引用表 + Hosting=0 红线 + 事件 topic 字节冻结（`OnlineDependencyMatrixTests` 目录矩阵已随双工程退役）。

### Breaking
* Actor 遍历失败可见性（C334，GFX-1044 / GitHub #672）：`ActorManager.ActorForEach(Action<IActor>)` 返回值由 `void` 改为 `int`（回调抛出异常的 Actor 数量，0 = 全部成功，逐条 `Core.ActorManager.ActorForEachError` 日志保持）；`ActorForEach<T>(Func<T, Task>)` / `ActorForEach<T>(Action<T>)` 更名为 `ActorForEachAsync<T>` 并改由 `SendAsync` 投递后逐个等待汇总，返回 `Task<int>` 失败计数（原 `Tell` 发后不管语义删除，超时以 `TimeoutException` 计入失败）。三个重载仓内零调用，无迁移面。
* RPC 消息处理器取消贯通的破坏面（C307 / GFX-1019，GitHub #646）：`BaseRpcMessageHandler<TRequest,TResponse>.ActionAsync` 抽象签名增加第三参数 `CancellationToken cancellationToken`（超时或外部取消时触发，业务实现应在协作式取消点检查并停止写入响应）；`InnerActionCoreAsync` 同步增加该参数（internal）。全部 `ActionAsync` 覆写（含 Server 仓消费方与 gfx-role 模板）须同步追加参数。
* StateComponent 关服回存注册 API 签名变更（GFX-1026）：`StateComponent.AddShutdownSaveFunc(Func<bool, bool, Task>)` 改为 `AddShutdownSaveFunc(Type stateType, Func<bool, bool, Task>)`——注册表以状态类型为键幂等替换（热更后同一状态类型重复注册替换旧委托而非追加叠加执行）；自定义调用该注册入口的消费方需补传 `typeof(TState)`
* QuartzTimer / BaseComponentAgent 定时器公开 API 异步化（C330 / GFX-1041）：`QuartzTimer.Remove/Pause/Resume`（含批量）→ `RemoveAsync/PauseAsync/ResumeAsync`（单个重载返回 `Task<bool>`）；`QuartzTimer.Schedule/Delay/WithCronExpression/Daily/WithDayOfWeeks/Weekly/Monthly` → 同名 `XxxAsync` 返回 `Task<long>`；`BaseComponentAgent.Unscheduled/Pause/Resume` → `UnscheduledAsync/PauseAsync/ResumeAsync`，`Delay`（3 重载）/`Schedule`/`Daily`/`Weekly`/`WithDayOfWeeks`/`Monthly`/`WithCronExpression` → 同名 `XxxAsync` 返回 `Task<long>`。旧同步名删除、无别名；下游消费方（Server 运行仓）需同步改名并 await。
* 发现层装配入口异步化（C333，GFX-1039，GitHub #667）：`MongoDiscoveryRuntime` / `PostgreSqlDiscoveryRuntime` / `InProcDiscoveryRuntime` 的 `public static void Activate(DiscoveryActivationOptions)` 改为 `public static Task ActivateAsync(DiscoveryActivationOptions, CancellationToken = default)`，装配阶段全部 `GetAwaiter().GetResult()` 同步阻塞消除——带 `SynchronizationContext` 的宿主（Unity 主线程 / WPF / 遗留 ASP.NET）不再依赖链路 ConfigureAwait 纪律规避死锁，宿主线程在装配 I/O 期间保持可用；`DiscoveryRuntimeResolver.Activate` 同步改 `ActivateAsync`（internal 测试缝 `ActivateFromTypeName` → `ActivateFromTypeNameAsync`），反射约定签名同步更换；`AppStartUpGameServerBase.ActivateDiscoveryLayer()` → `protected virtual Task ActivateDiscoveryLayerAsync()`（`StartAsync` 内 await，启动序不变）。同步入口删除、无 `[Obsolete]` 垫片（消费仓核对零直调）；参数校验异常对 await 调用方观察形态不变（async 实现经任务上抛、非 async 的 InProc 同步抛出）。
* 全局事件派发串行化破坏面（C339，GFX-1047 / GitHub #675）：`IEventListener.HandleEventAsync(GameEventArgs)` 无代理重载删除（接口与 `EventListener<T>` 基类，包消费方直接调用该重载将编译失败；监听器调用一律经所属 Actor 上的组件代理）。
* 服务器间端点静态引导退役的破坏面（C304，GitHub #643）：
  - 公开 API 签名：`IServiceEndpointResolver.ResolveEndpoint` / `ITransportProtocolAdapter.GetOrCreateStreamAsync` / `IsServiceAvailable` 的目标参数由 string 改为 `RemoteServiceTarget`；`IRemoteMessageClient` 新增 `SendOneWayAsync(RemoteCallContext, MessageObject)`（string 形态保留，语义 = ServerType 级确定性选择）；`RemoteCallContext` 新增 `TargetInstanceId`；`UnifiedMessageSender` / `UnifiedMessageSenderHolder.InitializeWithDefaults` 构造参数变更（新增必填 `routeTable`，本服身份改 `localServerType` + `localInstanceId`）；`RemoteEndpointNotFoundException` 携带 `Target`（原 `ServiceName` 删除）。
  - 删除类型：`EnvironmentVariableEndpointResolver`、`EnvironmentServiceInstanceDiscovery`、`RemoteMessagingBuilder.BuildDefault()` 与无参 `BuildFromEnvironment()` 静态工厂（生产走实例链 `new RemoteMessagingBuilder().LoadFromEnvironment().WithRouteTable(...).UseKcpTransportAdapter().Build()`）；未调用 `WithRouteTable` 时 `Build()` 在运行时抛 `InvalidOperationException`（fail-fast，无回落）。
  - `services__{Role}__{scheme}__{index}` / `services__{Role}_{InstanceId}__tcp__0` 环境变量契约失效；SQLite 拓扑只支持单进程（跨进程 RPC 不再可用）；`docker-compose.multi.legacy.yml` 删除。
  - `EndpointNotFound` 归入不可重试且不计入熔断与健康评分（冷启动窗口内的调用直接失败而非拉黑 Role；需重试的调用方改为上层重试）。`ServerSendOptions.AllowRetry` 默认 false，影响面限于显式开启重试的调用方。
  - `player_route.InstanceId` 语义变更：从占位值改为权威发现层实例 Id（`PlayerSessionManager.LocalDiscoveryInstanceId` 装配缝注入，仅观察形态跳过写入）；滚动升级窗口内旧版本写入的占位行按 offline 处理，玩家重新登录后自愈；`PlayerRouteResolver.ExtractServerId` 与 `Game.Id` 回落删除。
  - 混布约束：开启 KCP 广播（`IsEnableInnerKcp`）之前，集群所有节点必须升级到本版本或更高（旧版本 Mongo 读取方对未知字段抛 `FormatException`，watcher 保留旧快照、新节点不可见）。


* `OnlineCapabilityOptions.UseGameDbStores` 由 `bool` 改三态 `bool?`（C302）：null = 配置未声明（触发启动期告警后按 InMemory 处理）、true = 落库、false = 显式选择易失；消费方需按可空布尔判读（仓内调用点已同批迁移）。
* Capability 能力注入机制重设计（C294，GFX-998，破坏性一次性切换，能力未上线无兼容层）：
  - 模块契约改为三段生命周期：`ICapabilityModule` 新增 `ActivateAsync(CapabilityActivationContext, CancellationToken)` / `DeactivateAsync(CancellationToken)`；激活按拓扑序执行、中途失败逆序回滚，停止为停调度 → 逆序 Deactivate → 机制自动撤销全部桥。四桥发布、榜单预建、撮合通知订阅、旧好友数据迁移迁入所属能力模块。
  - 桥机制化：泛型 `CapabilityBridge<T>.TryAcquire` / `CapabilityBridgeLease<T>`（撤销 / 替换后抛 `CapabilityBridgeUnavailableException`，族层映射 8002），发布只经 `CapabilityActivationContext.Bridges.Publish<T>`，同契约重复发布 fail-fast；删除 `Online.Abstractions/Bridges/` 四对 holder/lease 与 Launcher 四个 `*BridgeWiring`。
  - 服务注册改为描述符表（类型键 + 命名键 + 懒工厂 + 来源），替换实现走显式 `CapabilityCompositionOptions.Override<T>`；删除 `TryGetService` / `AddService(旧)` / `UseService` 与全部探测回退，可选依赖改 `GetOptionalService<T>`（仅 manifest 声明的可选能力缺席时返回 null），扩展点枚举 `GetServices<T>` 仅 Activate 期可用。
  - 族组合骨架：`CapabilityComposition.Compose(ICapabilityFamily, CapabilityCompositionOptions)` 取代 `AddOnlineCapabilities` / `OnlineCapabilityComposition(Options)` / `OnlineAdminApiComposition`；Online 族实现 `OnlineCapabilityFamily`（族基建含管理面适配器）；admin 组装上移 `Hosting.AdminApi`，同名 action 直接失败，声明 / 实注册双向对账；新增元端点 `list_capability_services` / `list_capability_settings`，`list_capabilities` 增依赖状态与缺失功能。manifest 补声明 `query_leaderboard`（online-competition）、`query_match_settlement`（online-match），admin action 共 35 个。
  - 依赖分必选 / 可选，任何 `Register` 前统一检查：必选缺失或成环打 Error 并抛 `CapabilityAssemblyException`（一次报全、环路列完整路径），可选缺失打 Info「缺失功能「…」，不影响 … 其余功能运行」；同层按能力 Id 序数定序。依赖边：match → assets / matchmaking、competition → assets / match、operations → assets / social / match / matchmaking 由必选改为可选。
  - 参数：默认值只在能力包 Options 根类型定义（新增 `OnlineFriendOptions`，删除 `OnlineAssetsOptions` 与全部 `?? new XxxOptions()` 兜底），装配按「包内默认 → `Online.Settings.<能力Id>` → 代码 `Configure<T>`」逐字段覆盖后 `Validate`，未知能力 Id / 未知字段 / 类型不符 fail-fast。回调：宿主扩展点 `RequireHostService<T>`、Hotfix 钩子 `DeclareHook<T>` + `CapabilityHook<T>.Attach`（逐调用读取当前实现，聊天审核钩子已接线）。
  - 事件：删除 `CapabilityEventJob` / `CapabilityEventNotification` / `RaiseEvent`，业务事件统一机制层 `CapabilityEventBus`（Register 期订阅、Activate 前拒绝发布），topic 重整为 `<族>.<域>.<事件>`。调度器支持 `ISchedulerClock` 注入、tick job 逐个隔离并记录日志与指标，停止后再 `Start` 抛 `InvalidOperationException`。
  - 启动：`AppStartUpGameServerBase` 模板新增 `ComposeCapabilitiesAsync`（热更装载之后、网络监听之前，同族进程内单例）；`AppStartUpGame` 删除三个 Online 编排方法与两个字段。
  - 配置：`AppSetting.IsEnableOnline` / `IsEnableOnlineAdmin` 删除，并入 "Online" 段 `EnableRuntime` / `EnableAdminApi`，新增 `EnableRoles`（缺省 `["Game"]`）与 `Settings`；删除键 `EnableMatchGameBridge` / `FriendBridgeSeedPlayerIds` / `MatchBridgeLeaderboardId`（迁入 `Settings.online-competition.LeaderboardProjection`）。迁移写法：CLI `--IsEnableOnline` / `--IsEnableOnlineAdmin` 改为 `Configs/app_config.json` 对应 Role 段 `"Online": { "EnableRuntime": true, "EnableAdminApi": true }`。
  - 护栏：GFX0018 改为按泛型符号判定（替代名字清单），覆盖 `Bridges.Publish<>` / `TryAcquire` / `Attach` / `Override<>` / `Configure<>` / Register 期专用 API；GFX0017 机制白名单与 `IsMechanism` 收敛为同一集合（`Hosting.Core` 属机制包）；`IOnlineChatModerationHook` 及消息 / 裁决类型上移 `Online.Abstractions`，Hotfix 不再引用 Social 实现包。退役 22 个 `Launcher.Online*` 本地化键。

* 全解决方案 Task/ValueTask 成员方法统一 Async 后缀（C290，204 处、15 工程，零行为变更）：返回 `Task` / `ValueTask` 的接口与类成员方法（任意可见性，含 `Task<T>` / `ValueTask<T>` 任意泛型深度与元组形态）一律 `Async` 后缀，接口 / 虚链整链一致改名——`IActor` / `IComponentAgent` / `IEventListener` / `ITimerHandler` / `IMessageHandler` / `IHotfixBridge` / `IDatabaseService` / `IHttpHandler` / `IPlayerSessionManager` / `IWorkerActor` 及其全部实现链（如 `SaveAll→SaveAllAsync`、`TimerSave→TimerSaveAsync`、`InnerAction→InnerActionAsync`、`Enqueue→EnqueueAsync`、`IDatabaseService.Open/Close→OpenAsync/CloseAsync`、`GameDb.Init→InitAsync`、`UpdateSession→UpdateSessionAsync`、`HttpHandler.HandleRequest→HandleRequestAsync`、`IHttpHandler.Action/ActionMessageObject→ActionAsync/ActionMessageObjectAsync`，权威映射 = gfx-doc archived C290 rename-map.md）。撞名配对按 `InitCore`/`CloseCoreAsync` 先例以 `CoreAsync` 消解：`QuartzTimer` 私有 `StopAsync→StopCoreAsync` 让位公开 `Stop→StopAsync`、`AppStartUpByServer.StartServer→StartServerCoreAsync`、Base Handler 既有 `InnerActionAsync→InnerActionCoreAsync` 让位公开编排入口。豁免：外部契约实现（Quartz `IJob.Execute` / TouchSocket `MiddlewareBase.RegisterSession`/`UnRegisterSession`）、名已含 `Async` 记号、局部函数、测试方法、`GameFrameX.ProtoBuf.Net`（fork，用户裁定不处理）。对外破坏性（公开 API 批量更名）：受影响 API 全部在解决方案内（含 Tests）闭环消费、无外部消费者；XML doc / cref、本地化值内嵌方法名（键名不动，en + zh-CN 双 resx）、WrapperGenerator 发射模板与跳过表、测试反射字符串随迁；验证 = 构建 0 错 0 新增警告 + 全量测试与基线零漂移 + 语义级复扫生产代码归零 + 605 hunk diff 逐 hunk 纯改名审计（0 控制流丢失）。

* 组件代理事件派发扩展下沉 Core 并退役服务器段广播（C289，GFX-991）：`EventDispatcherExtensions`（`agent.Dispatch(eventArgs)`）自 `GameFrameX.Hotfix/Common/Events/` `git mv` 至 `GameFrameX.Core/Events/`（命名空间 `GameFrameX.Hotfix.Common.Events → GameFrameX.Core.Events`），删除生产零调用的服务器段广播分支（原 `IServerScopeEvent` 载荷经 `ServerComponentAgent.OnlineRoleForeach` 遍历在线玩家派发），自处理与静态 `EventDispatcher` 共用同一监听器循环 `DispatchToActorAsync`（形参 `Actor → IActor`、`private → internal`），日志键统一 `Core.EventDispatcher.*`（退役 `Hotfix.Event.SelfHandle*`）。对外破坏性（公开 API 删除）：`IServerScopeEvent`、`ServerComponent.OnlineSet`、`ServerComponentAgent.AddOnlinePlayer` / `RemoveOnlinePlayer` / `IsOnline` / `OnlineRoleForeach`、扩展命名空间变化；迁移写法——在线判定改 `PlayerSessionManager.Instance.GetByPlayerId(playerId) != null`（在线人数 / 列表 `Count()` / `GetPageList()`，跨服路由 `TryGetPlayerRoute`），扩展引用改 `using GameFrameX.Core.Events;`（组件代理侧调用写法 `this.Dispatch(...)` 零变化），`IServerScopeEvent` 无替代（组件代理侧不再广播，需要遍历在线玩家时以会话层为在线事实源自行派发）；`ServiceOnlineEventArgs` / `ServiceOfflineEventArgs` 去掉该接口实现后静态派发行为不变。
* 统一消息发送器本服身份改为构造注入（C286，GFX-988）：`UnifiedMessageSender` 构造器新增必填 `localServerType`（空白即抛 `ArgumentException`）/ `localServerId`（置于 `instanceSelector` 之后、可选 `metrics` 之前），`UnifiedMessageSenderHolder.InitializeWithDefaults` 签名同步并透传；类内删除构造期读取 `GlobalSettings.CurrentSetting` 与 `GameServerConst.Game` 常量回落——发送端本服判定（`SendToPlayerAsync` Step 3）与指标服务名兜底不再依赖「桥接构造前把 CurrentSetting 钉回首启动 Role」的隐式时序，`--AllInOne` 多 Role 进程下身份取自热更桥接 `OnLoadSuccess` 收到的首个装载 Role 的 `ServerType` / `ServerId`，现网所有流程行为等价。对外破坏性：公开构造器与 `InitializeWithDefaults` 签名变化（RemoteMessaging 为可打包程序集），旧签名不再编译；仓内调用方（热更桥接装配点 + 测试构造）已同批迁移。


* Online 叶子域命名空间迁移映射（C231 P3 拆包；无 NuGet 包消费者，破坏面纯源码级，仓内消费者 = 单体剩余 9 目录 / Runtime 壳 / Tests，已全量随迁）：`GameFrameX.Online.{Assets,Social,Matchmaking,Party} → GameFrameX.Capability.Online.*`（143 域文件，分归 Assets 34 / Social 67 / Matchmaking 29 / Party 13，另 handler 与助手文件随包）；`GameFrameX.Online.Runtime.AdminApi.{OnlineAdminApiContract,OnlineAdminApiAdapters} → GameFrameX.Capability.Online.Contracts`；`GameFrameX.Online.Audit.{OnlineAuditQuery,OnlineAuditRecord,OnlineAuditPage,OnlineAuditDomain} → GameFrameX.Capability.Online.Contracts`；`OnlineRuntimeHost` 叶子域 typed 属性（AssetStore/AssetTransactionStore/AssetQuery/GrantService/ChatStore/FriendStore/GroupStore/NotificationStore/ReportStore/SocialGraphStore/MatchListingStore/MatchTicketStore/PartyStore/SocialDecisions/Punishments/Notifications/MatchTickets/Matchmaker/MatchQueueObserver/ChatAudit）退役——产物经能力模块自装配与 `CapabilityHost.Services` 取用（P4 域属性保留：Timeline/Overview/Seasons/Tournaments 在叶子能力被禁用配置下可为 null，其 handler/job 随模块禁用不可达）
* P4 上层域命名空间与组合根类型迁移映射（C232 P4；无 NuGet 包消费者，破坏面纯源码级，仓内消费者 = Launcher / Tests，已全量随迁）：`GameFrameX.Online.{Match,Leaderboard,Season,Tournament,Audit,HotfixRollback,GameEvents,Timeline,Overview} → GameFrameX.Capability.Online.{Match,Competition.Leaderboard,Competition.Season,Competition.Tournament,Operations.Audit,Operations.HotfixRollback,Operations.GameEvents,Operations.Overview,Operations.Timeline}`；`GameFrameX.Online.Runtime`（LiveOps 域类型）`→ GameFrameX.Capability.Online.Competition.LiveOps`；`GameFrameX.Online.Runtime` 命名空间下的组合根类型（OnlineCapabilityCompositionOptions / OnlineCapabilityComposition / OnlineAdminApiComposition）`→ GameFrameX.Launcher.Online`；`OnlineRuntimeHost` 退役——`OnlineCapabilityComposition.Runtime` 改为机制公开面 `CapabilityHost`（StartAsync/StopAsync 同签名同幂等语义，AppStartUpGame 调用形态零变化）
* MessageOperationType 完全退役（C205）：删除枚举与特性第二参数、MessageProtoHelper.OperationType 注册表与 GetMessageOperationType、INetworkMessageHeader/MessageObjectHeader/MessageObject 的 OperationType 属性链与日志 OpType 字段；心跳判定统一改型为 MessageProtoHelper.IsHeartbeat(MessageType)（IHeartBeatMessage 标记派生）。包头 14 字节长度不变，第 5 字节退役为保留字节（恒 0），线上字节级兼容；对外为源码级破坏性（公共属性/方法/枚举删除）

### Removal
* 删除同步 `GameDb.Close()`（C267 / GFX-907）：sync-over-async（`GetAwaiter().GetResult()`）阻塞缺陷本体，全仓与消费仓零调用；异步 `GameDb.CloseAsync()` 为唯一关停入口（现含注册表卸载语义）

* 删除 GameFrameX.Apps.ActorType 死枚举（C219）：运行时零引用（账号 id 实际由 ActorIdGenerator.GetUniqueId() 生成），作用域机制维持 GlobalConst.ActorType* 常量现状，并在 GlobalConst 与 ComponentTypeAttribute 的 XML doc 补区间规则（<128 每实体多实例 / >128 每服单例）；非破坏性——两仓成员引用已验证为零，Apps 工程不进 NuGet 发布面
* 删除 GameFrameX.Online 与 GameFrameX.Online.Runtime 双工程（C232 / C227 P4 终态）：单体剩余 9 上层/聚合目录与过渡壳内容已全量随 P3/P4 迁入 11 包（Hosting 3 + Online 8）；slnx 摘除、csproj 与源文件物理删除（git rm）；`bin/` 18 个陈旧产物（工程已不在 slnx，dotnet clean 无视）手动清除后 rebuild 零再生（lesson `msbuild-stale-output-after-project-removal` 正反断言）；`OnlineDependencyMatrixTests` 目录依赖矩阵随目录消失退役（topic 字节冻结断言保留）
* 删除 NotHotfixTimerHandler 死代码三件（C343，GFX-1053 / GitHub #681 M40）：public abstract `NotHotfixTimerHandler` 类（全仓零子类零引用，`QuartzTimer` 仅支持热更 `ITimerHandler` 调度路径）、`QuartzTimer` 中整块注释的「非热更定时器」7 个泛型重载（`Schedule` / `Delay` / `WithCronExpression` / `Daily` / `WithDayOfWeeks` / `Weekly` / `Monthly`，引用已不存在的 `Param` 类型，取消注释无法编译）与 private `GetJobDetail` 非热更重载；`ParamKey` / `TimerJobHelper` / 热更 `GetJobDetail` 不动。破坏性：`GameFrameX.Core` 为 NuGet 发布包，外部若继承该抽象类将不再编译（仓内无消费者，无迁移面）
### Refactor
* 全仓执行单类型每文件与大括号规则（C290，纯代码组织，行为零变更）：89 个多顶层类型文件拆分为单类型文件（Hotfix 26 / Apps 21 / Capability 11 / Tests 7 / Core 6 / RemoteMessaging 5 / 其余 13；块级 namespace 遗漏的 4 个 Mail HTTP Handler 一并补拆）；82 处 `using var` / `await using var` 改为 `using (...) { ... }` 块形式，`#if XNUMBER_CHECK` 内 4 处单行 if 补大括号；19 个「文件名 ≠ 类型名」文件 `git mv` 更名（含 `GameDB→GameDb`、`Singletion→Singleton`、`FP` 的 4 个 partial 切片、gfx-role 模板 `{{Name}}Handlers.cs→Req{{Name}}EchoHandler.cs`）；`GameFrameX.Architecture.Analyzers` 新增代码风格分析器（`Analyzers/CodeStyle/`：GFX0019 单类型每文件 / GFX0020 文件名与类型名一致 / GFX0021 禁止 using 声明，严重级别在 `.editorconfig` 管理）+ `EnforceCodeStyleInBuild` 构建期强制；`Directory.Build.targets` 收敛为单一入口全仓注入 Architecture.Analyzers（取代原 `GameFrameX.Capability.*` 条件块与 Apps/Hotfix/Launcher 的显式引用）；vendored `GameFrameX.ProtoBuf.Net` 与 ProtoExport 生成物 `GameFrameX.Proto/Proto/*.cs` 按路径豁免。Roslyn token 级等价校验 26 个变更工程与 HEAD 完全一致。
* 热更桥接实例去除启动基类继承并更名为生命周期桥接（C285，C284 后续）：全仓唯一 `IHotfixBridge` 实现 `AppStartUpHotfixGame` 更名迁移为 `internal sealed HotfixModuleLifecycleBridge`（`GameFrameX.Hotfix/Lifecycle/`，命名空间 `GameFrameX.Hotfix.Lifecycle`）——`AppStartUpBase` 继承在 C284 后全为死重（no-op `StartAsync` 覆写、宿主已执行过的冗余 `AppBootstrapper.EnsureInitialized` / `InterNodeAuthentication.Configure` 装配、无人触碰的就绪屏障与退出令牌），新类不再属于启动 Role 体系；`Init` 内非冗余的 `GlobalSettings.SetCurrentSetting(setting)` 显式化保留并注释——多 Role 进程（`--AllInOne`）下把 `CurrentSetting` 钉回首启动 Role 的配置，且必须早于统一消息发送器构造固化本服身份（单 Role 形态无差异，行为等价）；删除 no-op `StartAsync` 覆写、`using GameFrameX.StartUp;` 与整文件注释死块 `AppStartUpHotfixGameByGateWay.cs`；`GameFrameX.Hotfix.csproj` 移除对 `GameFrameX.StartUp` 的 ProjectReference（构建产物元数据正向证明：热更程序集引用表不含启动程序集，桥接类基类为 `System.Object`）；`IHotfixBridge` 公开契约不变，`HotfixModule.ParseDll` 按接口定位机制不变

* Online 依赖解环与方向治理（C223，能力簇拆包前置 P0）：`Events/` 收窄为事件总线抽象（OnlineEvent / IOnlineEventPublisher / OnlineEventPublisher / OnlineEventConsumer / OnlineEventSanitizer / OnlineEventEnvelopeMapper / OnlineEventAuditView，与域类型零引用）；13 个域事件工厂随域迁移并载荷原始化（工厂签名领域类型参数 → 原始字段参数，topic 常量与信封语义逐字节不变）；跨叶子协作契约上移 Contracts（IOnlineSocialGate / OnlineSocialInteractionPurpose / OnlineSocialDecision / IOnlineChannelMembershipProbe / OnlineChatChannelKind）。程序集内目录依赖成为严格向下 DAG（含三节点环 Events→HotfixRollback→Audit→Events 解除），新增依赖矩阵门禁测试（OnlineDependencyMatrixTests：using 语义解析 + 允许方向表 + 叶子簇横向零互引 + topic 常量冻结断言，零豁免）。公共类型命名空间迁移映射（程序集内迁移，无 NuGet 包消费者，破坏面纯源码级，消费者仅 GameFrameX.Online.Runtime 与 Tests）：
  `GameFrameX.Online.Events.OnlineAssetEvents → GameFrameX.Online.Assets`；
  `OnlineChatEvents / OnlineNotificationEvents / OnlineSocialEvents → GameFrameX.Online.Social`；
  `OnlineMatchEvents → GameFrameX.Online.Matchmaking`；
  `OnlineMatchRuntimeEvents → GameFrameX.Online.Match`；
  `OnlinePartyEvents → GameFrameX.Online.Party`；`OnlinePresenceEvents → GameFrameX.Online.Presence`；
  `OnlineSeasonEvents → GameFrameX.Online.Season`；`OnlineSessionEvents → GameFrameX.Online.Session`；
  `OnlineLeaderboardEvents → GameFrameX.Online.Leaderboard`；`OnlineTournamentEvents → GameFrameX.Online.Tournament`；
  `OnlineHotfixEvents → GameFrameX.Online.HotfixRollback`；
  `GameFrameX.Online.Social.{IOnlineSocialGate, OnlineSocialInteractionPurpose, OnlineSocialDecision, IOnlineChannelMembershipProbe, OnlineChatChannelKind} → GameFrameX.Online.Contracts`
* 统一 CacheState 为 Provider 中立基类：业务状态类改继承 BaseCacheState，Mongo 序列化指令（ConventionPack + ClassMap 懒注册）内聚到 GameFrameX.DataBase.Mongo（C171）
* BaseCacheState.ToBytes 由抽象改为 JSON/UTF8 默认实现，StateHash 脏检查与持久化格式解耦；删除无引用的 BeforeSaveToDb
* Apps/Hotfix 移除数据库实现程序集引用与 MongoDB GlobalUsings；移除 BsonClassMapHelper / CacheStateTypeManager 与孤儿本地化键
* 架构分析器移除 Mongo CacheState 符号豁免（GFX0001）；GFX0016 不变

### Changed

* 升级 GameFrameX.Foundation 全部包引用 4.1.1 → 5.0.0（C224，7 项目 13 处）；StartUp 新增 GameFrameX.Foundation.Logger.GrafanaLoki 组件包，GameApp 的 Grafana Loki 接线迁移为 UseGrafanaLoki 显式注册（Url/用户名/密码取自 CLI 选项、Labels 取自 GrafanaLokiLabelTag 反射收集，LogIsGrafanaLoki=false 时零注册；CLI 选项面与 sink 默认值与 4.1.1 一致）。Foundation.Logger 5.0.0 起不再传递 MongoDB.Driver / Serilog.Sinks.MongoDB（Serilog.Sinks.Grafana.Loki 仅由 Logger.GrafanaLoki 组件包引入）
* 统一启动形态为「宿主持监听 + 热更持业务生命周期」（C284，Game 归一 Social 形态）：缺省 `StartRoleServicesAsync` 由同步 `LoadHotfix` 改为 `await LoadHotfixModule`——全 18 Role 装载即激活热更生命周期（配置表/定时器/全局组件经桥接 `OnLoadSuccess`；此前不含 Game 的 Role 组合单起时全部不执行，Social 业务落地热更当天会踩），`--AllInOne` 重复装载由同路径幂等守卫短路；`AppStartUpGame` 不再自拟启动序列（`base` 缺省调用 + Online 双闸附加），KCP 登录流白名单（`ReqLogin/ReqPlayerList/ReqPlayerCreate/ReqPlayerLogin/ReqSessionResume`）与 `OnConnected`（MaxClientCount 拒绝 + `PlayerSession` 建立）/`OnDisconnected` 会话钩子上移宿主覆写，`PackageHandler`/`OnStartupCompletedAsync`/`OnStoppingAsync` 三覆写退役；断线业务段（房间断线标记 + 会话恢复通知）经新增共享事件 `Core.Session.PlayerDisconnectedEventArgs` + 热更 `PlayerDisconnectedEventListener` 全局监听器桥接；热更侧 `AppStartUpHotfixGame` 的 `OnLoadSuccess` 收窄纯生命周期激活（`RunServer`/`StartAsync`/`ReplyHeartBeatAsync` 退役，ByMain/ByHeart 删除）、bridge `Stop` 收窄纯业务清理（Actor/定时器/会话），监听不再活在可收集 ALC 内（消除热切换 Retire 卸载验证必然失败与 ALC 泄漏）；`ServiceOnline`/`ServiceOffline` 单源化为宿主缺省分发（热更侧分发删除，避免非首装载 Role 双派发），Game 起服由两组启动序列归一为一组。

### Bug Fixes
<<<<<<< HEAD
* 顶号时在获取登录锁后及异步通知返回后复核新会话；新会话已断开或被替换时保留原会话，避免无效登录踢掉有效连接。并发登录测试的同步等待增加超时并观察登录任务失败，避免断言失败后永久等待。
* 顶号流程按 playerId 串行化（C306，GFX-1018 / GitHub #647）：`PlayerSessionManager.UpdateSessionAsync` 的读旧会话→通知→关闭→移除→绑定→路由上线关键段此前无互斥，同玩家并发登录可双活绑定、互踢/自踢，路由版本交错重复递增致消息路由错误；现经 `PlayerLoginLocks`（`ConcurrentDictionary<long, SemaphoreSlim>`）串行——锁协议为 GetOrAdd→WaitAsync→映射复核（失败让位重取）→关键段→持锁 TryRemove(键值对)→Release，持锁回收使锁表仅驻留 in-flight 登录条目（天然有界，规避计数/等待数探测式回收固有的 check-then-remove 竞态）；另加同会话重登守卫（`oldSession.SessionId == sessionId` 不再自通知/自关闭，幂等重绑）。公开接口零变化；新增确定性竞态放大并发回归测试 `PlayerSessionManagerConcurrentLoginTests`。
* BaseRpcMessageHandler 响应对象池泄漏与超时数据串台修复（C307 / GFX-1019，GitHub #646）：`InnerActionAsync` 从 `MessageObjectPoolHelper.Get` 借出的响应对象此前在成功/超时/异常路径均不归还（直派生处理器每请求泄漏一个池化对象，对象池持续耗尽），且超时后孤儿业务任务继续在后台写入同一实例。修复——超时经联动取消源取消业务任务并经 `ActionAsync` 透传取消令牌；归还规则按任务终态判定：业务任务到达终态（成功/失败/已取消）后归还对象池，仍在运行的孤儿任务所持有的实例不归还（交 GC 回收，池空时按策略新建），孤儿写入不可能到达下一个借用者；孤儿路径联动取消源不释放（防孤儿令牌注册回调触发 ObjectDisposedException）并观察孤儿任务异常。附带 `BaseRpcComponentHandler.Work` 通道关闭早退/写通道异常路径补归还（同族泄漏）。
* BaseRpcMessageHandler 响应对象池泄漏与超时数据串台修复（C307 / GFX-1019，GitHub #646）：`InnerActionAsync` 透传联动取消令牌；超时使用独立响应实例，避免仍运行的业务任务污染发送内容。业务响应和联动取消源在业务任务终态后回收，仍运行时不得提前归还；后台续体观察异常并释放资源。`BaseRpcComponentHandler.Work` 在通道关闭早退、写通道异常路径同样归还响应。新增迟到业务写入不影响超时响应的回归验证。
* 热更同步装载路径补退役编排，修复可收集 ALC 内存泄漏（C310，GFX-1021 / GitHub #649）：`HotfixManager.LoadHotfix` 重载时此前只切换 `CurrentModule` 指针并注册新模块，旧模块的可收集 ALC 永不进入退役链，`Unload`/GC 验证永不发生——每次同步重载泄漏一份完整热更程序集内存（类型对象、静态字段、元数据），长期运行内存随热更次数线性增长直至 OOM，且 in-flight 请求无法经 `FindRetiredOwner` 归属旧模块。现于 `Init` 成功且 reload 时在 `RegisterCurrent` 之前调用新增的 `HotfixHost.Retire(RetirementGracePeriod)`（internal 单点封装 `_moduleChain.Retire`，不走异步桥接回调），时序与 `LoadHotfixModuleAsync` → `LoadAsync` 逐语句对齐；首次装载路径零变化。
* BaseDataTable First/Last/Count 缓存热重载后失效（C311，GFX-1022 / GitHub #651）：`BaseDataTable<T>` 新增 `InvalidateCaches()` 重置首/尾/数量派生缓存标志，4 个生成表（TbItemConfig / TbAchievementConfig / TbSoundsConfig / TbLocalization）的 `LoadAsync` 在 `PostInit()` 前统一调用——热重载（Clear + 重填）后 `Count` / `FirstOrDefault` / `LastOrDefault` 不再返回旧缓存值，初次加载与重载同路径，调用方无需手动失效。已知限制：Luban 生成模板（`*.sbn`）不在本仓，上游模板需同步插入该调用
* Actor 移除时即使组件反激活抛出异常，也在 finally 中执行一次性回收回调，并保留原异常传播，避免调度任务与清理资源遗留。
* ActorManager 移除路径补齐一次性回收回调（C312，GFX-1023 / GitHub #652）：`RemoveAsync` 此前只 `actor.Tell(actor.InactiveAsync)`、`RemoveAllAsync` 只逐 Actor `InactiveAsync`，双双跳过 `OnRecycleAsync`——注册在 `_onceRecycleCallbacks` 的清理回调（如 Server 侧 BaseComponentAgent.QuartzTimer 的 ScheduleIdSet 清理）永不执行，Quartz 任务持续触发并向已移除 Actor 投递事件、内存与调度资源泄漏。两条路径对齐空闲回收 `RecycleIdleActorAsync` 的 Inactive → OnRecycle 固定顺序：`RemoveAsync` 改单 `Tell` 双 await（Actor 自身 Worker 队列内串行），`RemoveAllAsync` 逐 Actor await 两步（新增私有 `DeactivateAndRecycleActorAsync` 统一承载顺序契约）；公开签名零变化。回归测试固化 `ActorManagerRemoveRecycleCallbackTests`
* StateComponent 状态字典增删键统一为 ActorId（C313 / GFX-1024 / GitHub #650）：`StateComponent<TState>.StateDic` 登记用 `State.Id`、移除用 `ActorId`、回存派发反查 `state.Id`，三键不一致——`State.Id != ActorId` 的状态（子类 `ActiveReadStateAsync` 返回主键与 Actor 不同的状态）反激活删除落空永久驻留内存、定时回存因 `ActorManager.GetActor(state.Id)` 找不到 owner 被跳过、关服遍历僵尸状态写入无效数据。修复 = 登记改 `StateDic[ActorId] = State` 原子 upsert（同 Actor 重复读取/换键原地替换，不留残留条目）、`CollectActorWritesAsync` 改按键值对遍历以键（ActorId）查找 owner actor；`State.Id == ActorId` 主流路径键值等价行为不变，公开 API 零变化（StateDic 为 private）。
* StateComponent 关服回存注册热更后叠加执行且无超时（GFX-1026，GitHub #654）：`SaveFuncMap` 由 `ConcurrentBag` 改为 `ConcurrentDictionary<Type, Func<bool, bool, Task>>`，`StateComponent<TState>` 静态构造以 `typeof(TState)` 为键注册，重复注册（热重载后静态构造再次触发）替换旧委托——关服/定时回存不再对同一批状态叠加执行 N+1 次、注册表不随热更次数无界增长、并发重复写库与 actor 锁争用随之消除；`SaveAllAsync` 的 `Task.WhenAll` 追加 `WaitAsync` 整体超时（`ShutdownSaveTimeout` 缺省 120 秒，internal 供测试注入缩短），单个悬挂回存委托不再可能无限阻塞关服流程（超时异常经既有错误日志记录）。回归测试固化 `Tests/GameFrameX.Hotfix.Tests/StateComponentShutdownSaveRegistrationTests`（幂等替换 / 类型独立 / 超时上限三用例）
* StateComponent 关服回存注册改为按程序集简单名称与状态类型全名幂等替换，避免热重载生成不同 Type 身份后仍重复注册、保留旧代委托。`SaveAllAsync` 的整体等待上限为 120 秒，单个悬挂委托不能无限阻塞关服。回归覆盖同类型替换、独立类型、跨装载代次替换与超时。
* 配置表加载热重载支持与并发读者空表窗口修复（C314，GFX-1025 / GitHub #653）：`TablesComponent.LoadAsync` 改两阶段原子发布——4 张表新实例全部 `Task.WhenAll` 加载成功前不改动任何已发布状态（原实现先 `RemoveAllConfigs()` 清空映射、加载完成前逐表 `Add`，并发读者窗口期内拿到空 `IDataTable` 触发 NPE 或空数据；`if (IsLoaded) return;` 又使二次调用直接短路、热重载永久失效），成功后切换表字段引用 → `Refresh()` → 映射逐键替换 → 版本号递增；任一表加载失败旧数据零损伤。`IsLoaded` 改由加载版本号派生（`m_LoadedVersion > 0`）；`ConfigManager.AddConfig` 改 upsert（同键原子替换，免清空重建，`IConfigManager` 文档同步）；`TablesComponent.Init` / `ConfigComponent.LoadConfigAsync` 移除加载前 `RemoveAllConfigs()` 空窗语句（公共 API 保留）。已知限制：并发 `LoadAsync` 未串行化（唯一调用方生命周期桥为串行触发）；`SetTranslateText` 后热重载不重放 translator（仓内零调用方）；`TablesComponent` 为 Luban 生成物，生成器模板（`*.sbn`，不在本仓）需同步本修复。新增 `TablesComponentHotReloadTests` 4 用例（旧实现 2 用例必败）
* 配置表加载热重载支持与并发读者空表窗口修复（C314，GFX-1025 / GitHub #653）：4 张表全部加载成功后才逐表替换映射，加载失败保留已发布映射；同键替换是原子的，跨表读取不保证同一版本。`IsLoaded` 通过 Volatile.Read 读取加载版本，避免 volatile 字段按引用传递的编译告警。已知限制：并发加载未串行化，热重载不重放 translator；Luban 生成器模板不在本仓，重新生成前需同步相同行为。回归覆盖重复加载、加载期旧数据可读和失败保留。
* 生成包装方法签名补回泛型约束子句；override 方法不再重复输出继承的约束（重复输出会触发 CS0460）。新增编译级测试验证生成的 override / 非override 泛型方法声明可编译。
* 热更 Wrapper 生成器 ThreadSafe 单独使用不再静默跳过（C320，GFX-1030）：`[ThreadSafe]` 未同时标记 `[Discard]` 的组件代理方法此前在 `ShouldWrapMethod` 内直接跳过且无任何诊断，开发者误以为方法已被线程安全包装；现于 `ReportAttributeViolations` 输出 Warning 诊断（含类全名.方法名与两个特性名），跳过行为与生成产物零变化（`Logger` 新增 `LogWarning` 扩展，与 `LogError` 同形）。构建期对仓内 `ServerComponentAgent` 存量单独 `[ThreadSafe]` 用法报 4 处提示即为该陷阱实存证据；附 GeneratorDriver 回归测试（HotfixWrapperGeneratorThreadSafeWarningTests）。
* 三 Provider 环境事务改持有对象引用语义（C324 / GFX-1034 / GitHub #662）：Mongo / PostgreSQL / SQLite 事务壳的环境会话/环境事务槽由直接存 `AsyncLocal<会话/事务>` 改为存每壳新建的持有对象（Holder），壳 finally 先清 Holder 内部字段再清 AsyncLocal 槽——`AsyncLocal` 赋值后派生异步流（壳 action 内启动的 `Task.Run` 等各自捕获执行上下文快照）此前在壳结束后仍持有已提交已释放的 session/transaction，再开事务壳被嵌套判定误认为「已在事务内」而跳过自有事务与提交：真实服务器上写入得到 ObjectDisposedException，SQLite 单连接上写入静默落 autocommit（多写壳失去原子性）。统一以 Holder 内字段为嵌套判定与全部读取口径后，派生流在壳结束即同步观察到 null、再开壳为全新独立事务；字段保持实例级（多服务实例不互相污染语义不变）。
* GameDb.CloseAsync 关停改为逐库隔离：单个库 Close 抛出不再阻断其余库关停，全部尝试后统一上抛（单异常原样抛出，多异常聚合）；门面卸载行为不变。新增关停隔离回归测试。
* SQLite 连接闸不可重入死锁改为限时失败（C325 / GFX-1035，GitHub #663）：`SqliteDbService` 单连接串行闸（`SemaphoreSlim(1,1)`）此前全部获取点无超时——`ExecuteInTransactionAsync` 持闸期间 action 内 await 同实例不经环境事务放行的方法（`OpenAsync` / `CloseAsync`）会永久挂起。现全部 5 个闸获取点统一带 `GateWaitTimeout`（缺省 30 秒）超时，超时抛 `SqliteGateWaitTimeoutException`（`TimeoutException` 子类，新本地化键 `Database.Sqlite.TransactionGateWaitTimeout` 提示根因），且分类器将其排除出瞬时集、不被弹性引擎重试放大。action 内的数据操作（`FindAsync` 等）经环境事务放行，不受影响。**行为变更披露**：壳外操作等待一个持闸超过 30 秒的长事务时，现在也会以该异常失败（此前无限等待）。`ExecuteInTransactionAsync` 两重载 XML doc 补充限制说明。
* Sqlite NextSequenceAsync 遇 SQLITE_BUSY 启用幂等重试（C327，GFX-1037 / GitHub #665）：`NextSequenceAsync` 此前以 `isIdempotent: false` 接入共享弹性引擎，瞬时 `SQLITE_BUSY`/`SQLITE_LOCKED` 锁冲突零重试直接失败；改为 `true` 后按既有幂等写重试序列（{150,400,900} ms + jitter，共 3 次退避，与 Mongo/PG 缺省对齐）重试——发号为单条原子 `INSERT ... ON CONFLICT DO UPDATE ... RETURNING`，重试至多留空洞、绝不重复或回退；重试耗尽仍抛 `DatabaseUnavailableException`（内层保留 busy 异常，非静默）。新增 `SqliteNextSequenceBusyRetryTests` 真实 busy 回归（外部连接持锁 + 定时释锁，A/B stash 判别旧代码必败）
* QuartzTimer 等待 Quartz 调度器操作结果（C330 / GFX-1041 / GitHub #669）：`QuartzTimer` 此前丢弃 Quartz 4 `DeleteJob`/`PauseJob`/`ResumeJob`（`ValueTask<bool>`）与 `ScheduleJob`（`ValueTask<DateTimeOffset>`）的返回——`Remove` 返回时作业可能尚未删除（残留作业触发会把已回收 actor 以 isNew 重建），调度失败（如永不触发的 Cron）时调用方仍拿到"有效"ID，异常随被丢弃的 ValueTask 静默丢失。全部改为 `XxxAsync` 并 await：删除/暂停/恢复返回 `Task<bool>`（作业存在 = true；false 为正常结果不记 Warning，如一次性任务触发后被 Quartz 自动清理）、批量重载先拷贝 ID 快照再逐个删除（避免 await 期间 `ScheduleIdSet` 被修改）、调度方法仅在 `ScheduleJob` 成功后返回 ID；`BaseComponentAgent` 定时器封装层同步异步化，调度成功后才登记 `ScheduleIdSet`。回归测试 `QuartzTimerSchedulerResultTests`（永不触发 Cron 必须抛 `SchedulerException`，改前同场景不抛已 A/B 实证）。
* ActorIdGenerator 序号原子化并新增时钟回拨防护（C329 / GFX-1040）：`GetActorId` / `GetUniqueId` 此前在锁外读取共享 (秒, 自增) 状态——并发下可能生成重复 ID；序号耗尽时借用未来的秒，跨进程重启后可能重新发出历史 ID。现两类 ID 布局各持独立序号状态，推进与读取在同一临界区完成；序号耗尽改为在锁外等待下一秒（不再借用未来的秒，逻辑时钟不超前墙上时钟）；时钟回拨 ≤5 秒在锁外等待时钟追上，>5 秒抛 `InvalidOperationException`（新键 `Utility.Exceptions.ActorIdClockMovedBackwards`，区别于雪花 IdWorker 既有单参数键 `ClockMovedBackwards`）拒绝生成。**行为变更披露**：单秒容量耗尽（ActorId 每秒 4096 / UniqueId 每秒 524288）或发生 ≤5 秒回拨时，调用线程同步等待至多约 1 秒 / 5 秒；公开方法签名与 ID 位布局不变。
=======
* SQLite `TryAddUniqueAsync` 唯一性判定下沉引擎索引层（C326 / GFX-1036）：建表单飞内把 `[EntityIndex(Unique = true)]` 声明物化为 `json_extract(doc, '$."属性"')` 表达式唯一索引（索引名 = 状态类型名_声明名，同 Mongo/PG 引导语义；同名异构残留先删后建对齐声明）；唯一谓词可归约为 `x.属性 == 常量` 且自身值满足谓词时改走单语句 `INSERT … ON CONFLICT DO NOTHING` 快路径（零全表读、语句级原子、跨进程安全，`rowsAffected == 1` 判定），不可归约 / 未声明 / 自身值 null / 谓词常量与自身值不一致一律回落既有扫描事务语义，公共契约不变。配套两修：①环境事务回滚后建表就绪缓存整体失效——事务内首建的表与索引随回滚撤销而缓存仍标记就绪，后续操作撞「no such table」、或索引缺失令快路径静默放行重复键（DDL 均 IF NOT EXISTS，重建幂等）；②声明解析按类型进程级缓存，`TryAddUniqueAsync` 不再每次反射。行为变化披露：声明唯一索引的表，普通 `AddAsync` / `AddListAsync` / upsert 写入重复唯一值由静默成功改为引擎约束冲突（与 Mongo/PG 对齐）；存量 SQLite 库存在重复唯一值时首次访问显式抛 `SqliteException`（需先清数据再升级）

>>>>>>> 8380ea9f (fix(database): SQLite 唯一插入下沉引擎唯一索引层)
* SQLite json_extract 谓词下推的不可翻译成员不再被当作字符串成员下推：成员元数据缓存用哨兵承载「不可翻译」（`ConditionalWeakTable` 不接受 null 值），但解析出口曾直接返回哨兵，导致调用方以 null 判定「不可翻译」失效——`JsonIgnore` 等不可下推的字符串成员与常量比较会拼出 `COALESCE(, 0) = ?` 非法 SQL（运行期报错），与 null 比较则被折成恒假合取项静默丢行。现解析出口把哨兵还原为 null：不可翻译成员一律回退整表读并由客户端谓词权威判定。新增两条回归测试（不可翻译字符串成员的常量比较与 null 比较）。
* Sqlite Count/Any/FindPage 谓词下推，消除全表反序列化后内存过滤（C328 / GFX-1038）：`SqliteDbService` 读路径此前只下推 `x.Id == c`，其余条件一律整表 `SELECT` + 全量反序列化 + 内存求值。现将 filter 顶层 AndAlso 合取项（白名单：string 仅 `==`/`!=`、bool、整型、数值底层枚举，`Id` 映射 id 列）与软删可见性翻译为 `json_extract(doc, '$."Name"')` SQL 谓词：①全谓词精确可译时 `CountAsync` / `AnyAsync` 走 `SELECT COUNT(*)` / `EXISTS` 零反序列化，`FindPageAsync` / `FindSort*` 在排序键可译时走 `ORDER BY … LIMIT/OFFSET` + 独立 COUNT，仅反序列化单页行；②部分可译时下推可译项裁剪读取，客户端编译谓词保持权威重估，结果集不变；③不可译谓词（方法调用、OrElse、DateTime/Guid/decimal/浮点/ulong、嵌套成员、显式收窄转换、string 排序键）回退整表读，结果与修复前一致。精确性规则：非可空值类型缺键按 `default(T)`（`COALESCE(…, 0)`），可空成员 `!= v` 放行 NULL 行（对齐 C# 提升语义），排序次键 `id ASC` 复刻原稳定排序并列序、NULL 位置对齐 Mongo；值全参数绑定。10 万行无索引实测：Count 22–36 ms、Any ≤1 ms、FindPage 48–76 ms。**边缘行为披露**：精确快路径不再反序列化不参与结果的行，损坏的 doc 文本不再让 Count/Any 抛异常。新增回归测试 `SqlitePredicatePushdownTests`。
* 事件监听器关联组件未注册时不再抛裸 KeyNotFoundException（C340 / GFX-1050 / #678）：`HotfixModule.AddEvent` 原以索引器直读 `ComponentRegister.ComponentActorDic[compType]`，组件类型未经 `ComponentRegister.InitAsync` 注册（如组件所在程序集未传给 InitAsync）时抛出的 `KeyNotFoundException` 只含字典 key，不含是哪个监听器、哪个组件出问题，装配缺口无法定位。改为 `TryGetValue` 失败时抛携带监听器与组件类型全名的 `InvalidOperationException`（新本地化键 `CoreExceptions.Hotfix.EventListenerComponentNotRegistered`，中英双语）；不用 `ComponentRegister.GetActorType`（未注册与 Actor 类型 0 不可区分）、不改字典为并发集合（仅启动期写入、装载期只读，无并发写读）。Hotfix.Tests 补 `LocalizationAssemblyLoader`（与主套件同根因同修法：资源程序集不被 JIT 触达时 GetString 回落裸 key），回归用例固化 EventBindingTests 未注册失败路径。
* vec2/vec3/vec4 值类型相等成员补齐（C337，GFX-1048）：Luban 值类型 bean 生成的 struct 此前未实现 `IEquatable<T>` / `Equals` / `GetHashCode` / 比较运算符，落到 `ValueType` 反射实现——`Equals` 把 `Translator` 翻译委托也计入比较（同坐标一边调用过 `TranslateText` 即判不等），`GetHashCode` 实际只取首个字段（仅按 X，Y/Z/W 不参与，字典 key 冲突严重），比较器装箱。修复 = `GameFrameX.Config` 仓 `cs-dotnet-bin` / `cs-dotnet-json` 两套 bean 模板为 valueType 分支生成 `IEquatable<T>` + `Equals` / `GetHashCode` / `==` / `!=`（仅比较导出字段、排除 `Translator`；float 走 `EqualityComparer<float>.Default`（NaN==NaN 为 true）与 `System.HashCode` 聚合语义一致），本仓三个 vec 文件为再生成产物（模板提交见 Config 仓，类 bean 输出逐字节不变）；回归测试固化 `Tests/GameFrameX.Tests/Config/VectorValueTypeEqualityTests`（A/B：旧 struct 下 Translator/哈希判别 2/2 必败 + 运算符 CS0019 编译失败 → 修复后 9/9）
* 热更注册期构造校验与代理实例工厂（C338，GFX-1049）：`HotfixModule` 的 `Activator.CreateInstance` 裸创建统一收敛到注册期工厂 helper——组件代理的无参构造缺陷此前只登记 `Type` 不校验，推迟到组件首次取代理（`BaseComponent.GetAgent`）才以 `MissingMethodException` 爆出且无类型定位；现在 `ConvertHotfixAssembly` 装载期即校验（非抽象封闭类型 + 实现目标服务 + 公共无参构造，违例抛 `InvalidOperationException` 带实现类型与服务类型，新本地化键 `CoreExceptions.Hotfix.MissingParameterlessConstructor`），桥接/HTTP 处理器/事件监听器三处注册期创建同经校验。`GetAgent<T>` 改查注册期构建的强类型工厂表（`MakeGenericMethod` + `CreateDelegate` 实例化，不引入 `Expression.Compile` 全局编译缓存，随可回收 ALC 退役释放；启用包装且存在对应包装类型时工厂实现类型=包装类型），每次调用不再走反射激活，不可达的 null 检查删除。回归护栏 `HotfixModuleAgentFactoryRegistrationTests`（缺无参构造装载即抛 + 工厂表全覆盖与包装选择）。
* 组件激活失败可见性（C341 / GFX-1051 / GitHub #680）：组件激活异常原先在两层被静默吞掉（`Actor.ActivateComponentAsync` 两 catch 与 `ComponentRegister.ActiveComponentsAsync` 的 `// throw;` 注释），服务器误以为激活正常。修复——`ComponentRegister.ActiveComponentsAsync` 重写：未实现 Agent 的组件由「无上下文 NRE + Fatal」改为明确 Warning + 跳过（对齐 `ActiveGlobalComponentsAsync` 同情况处理），单组件激活失败逐条 Fatal 并计入失败清单，循环结束输出汇总 Fatal（Actor 类型 + 失败数 + 组件清单），`IsDebug` 时抛 `AggregateException`（含全部失败异常）；`Actor.ActivateComponentAsync` 两段 catch 在 `IsDebug` 时重新抛出（让第一层失败进入汇总与调用方）。非调试模式行为保持记录后继续；生产传播语义不变（如需另行决策）。新增本地化键 `Core.ComponentRegister.ComponentAgentNotImplemented` / `ActivateComponentsFailedSummary`；回归测试 `ComponentActivationFailureVisibilityTests`（A/B 判别例旧代码必败）。
* 热更 Wrapper 源码生成器修复并恢复组件代理 Actor 串行化（C292）：生成器引用的 Roslyn（5.6.0/5.9.0）高于 SDK 自带编译器，CS9057 下长期静默不加载，`HotfixModule.GetAgent` 回落到未串行化的原始代理。修复三件——生成类命名空间镜像代理源命名空间（`GameFrameX.Hotfix.Wrapper.Agent.<镜像段>`，同名代理 Player/Login 与 ServerRole/Login 的 `LoginComponentAgent` 分落互不冲突，此前固定单一命名空间 + 短名 hintName 在生成器真跑时即崩溃 CS8785）、hintName 由完全限定名单射净化（`.`→`_`、`+`→`__`）、分部类计数键改完全限定名；`Microsoft.CodeAnalysis.CSharp` 对齐 global.json 基线 5.0.0（与 Architecture.Analyzers 同约定）。**行为变更披露**：生成器恢复加载后，`[Service]`（非 `[ThreadSafe]`）等被包装的代理方法恢复经 `WorkerActor.EnqueueAsync` 在 Actor 队列串行化执行（并发调用可能排队），`[ThreadSafe]` 方法不受影响；新增装配护栏 `AgentWrapperAssemblyGuardTests` 防生成器再次静默停摆。全解决方案构建 0 错 0 警告（C291 遗留的 CS9057 已知例外随之关闭）。
* Mongo 事务提交最终失败后显式中止事务（C321 / GFX-1031）：`CommitTransactionWithRetryAsync` 在放弃提交（非可重试错误或重试预算耗尽）后此前仅依赖会话释放的隐式回滚，驱动不保证 Dispose 自身不抛异常，服务端事务状态可能悬置到超时。现于最终失败路径显式调用 `AbortTransactionAsync` 尽力而为中止（传 `CancellationToken.None`——清理不随常已取消的业务令牌一同取消），abort 自身失败仅记 Warning（新本地化键 `Database.Mongo.AbortTransactionAfterCommitFailed`，双语），原始提交异常照常包装为 `DatabaseUnavailableException` 上抛不被吞掉；提交成功路径与重试语义零变化。回归测试固化 `MongoDbServiceTransactionAbortTests`（手写 `IClientSessionHandle` 桩，A/B 判别旧代码必败）
* 全局事件派发串行化，消除线程池执行与空代理调用（C339，GFX-1047 / GitHub #675）：`EventDispatcher.Dispatch` 目标 Actor 不在内存时（actorId=0 或玩家 Actor 已回收）不再 `Task.Run` 在线程池执行监听器——改为按监听器 `AgentType` 反查所属 Actor 类型：全局 Actor（`ActorType > ActorTypeSeparator` 且已创建）`Tell` 投递到该 Actor 队列串行执行并传入真实组件代理；玩家段类型或全局 Actor 未创建记 Warning（新本地化键 `Core.EventDispatcher.DispatchOwnerActorNotLoaded`）后跳过。原全局分支下归属单个玩家 Actor 的监听器以 null 代理执行的 NRE（SessionRemoved）/`AgentIsNull`（PlayerSendItem）随之消除；`PlayerDisconnectedEventListener` 改在房间所属全局 Actor 上下文内直接使用传入代理。`EventDispatcher.Dispatch` 签名与全部调用点零改动。
* 空闲回收活跃时间字典修复（C345 / GFX-1054 / GitHub #682）：`ActorManager.CheckIdleActorAsync` 索引器直读 `ActiveTimeDic` 改 `TryGetValue`——未登记活跃时间的 Actor（全局 Actor 从不写入，仅玩家段 Actor 在 GetOrNewAsync/Get 登记）静默跳过，消除全局 Actor 开启 AutoRecycle 后每轮空闲检查抛 `KeyNotFoundException`；`RecycleIdleActorAsync` 复用 TryGetValue 结果计算回收窗口（删除索引器二次读取）；空闲回收成功移除 Actor 后同步 `ActiveTimeDic.TryRemove`，闭合字典随历史玩家数量无限增长（回收与 GetOrNewAsync 慢路径重写同 life-actor 串行，快路径双字典命中失败自然走慢路径重建）
* GameDb 门面生命周期三修（C267 / GFX-907）：①`GameDb.CloseAsync()` 关停即卸载——关闭全部注册库后清空 `MultiDbRegistry` 与门面静态状态（个别库 Close 抛出也经 finally 完成卸载，异常照常传播），close→reopen 同名重注册不再被残留注册表挡在 `AlreadyRegistered`，关停后门面 CRUD 回到未初始化快速失败而非使用已关停服务；②`InitCore` 的 `IsDefault` 提名 set-once 异名冲突回滚——`MultiDbRegistry.Unregister` + 释放已打开连接后重抛原异常，废除「冲突在注册后传播、靠进程退出兜底」旧语义，Init 失败不在注册表/门面留半初始化残留（新增 `MultiDbRegistry.Unregister(string)` internal，统一入口铁律：消费方仍只经 GameDb）；③删除同步 `GameDb.Close()`（`GetAwaiter().GetResult()` sync-over-async，线程饥饿/死锁面；全仓与消费仓零调用实证），异步 `CloseAsync` 为唯一关停入口。
* 连接池扩容判据改为 gate 排队深度（C354，GFX-1062 / GitHub #690）：TCP `EndpointPool` 与 KCP `KcpEndpointPool` 原以获取瞬间并发 `_inFlight` 决定扩容，本机低延迟建连下先后到达的并发获取互不可见、池退化为单连接（并发请求全打一条连接，`ConcurrentCalls_ShouldNotBeGloballySerialized` 间歇失败）。改判据为「存活数为 0，或（未到池上限 且 gate 等待队列深度 > 0）」：顺序调用永不扩容守住单连接复用，并发获取确定必扩容，与建连快慢无关；gate 仍串行化建连无惊群。并发回归用例注入 50ms 延迟建连工厂消除时序依赖，新增 `SequentialCalls_ShouldKeepSingleConnection` 顺序单连接回归。

* 三 Provider ExecuteInTransactionAsync 假事务修复（P0 / GFX-902 / C262）：事务壳此前与 action 内数据操作未绑定，无原子性——Mongo 会话未传给集合操作（提交空事务；standalone 回退还会把已落库的 action 重放一遍造成双写）、PostgreSQL 壳事务开在独立池化连接上（action 各操作走其它连接）、SQLite 事务壳内调用自带事务的批量/条件写 API 直接抛 "SqliteConnection does not support nested transactions"。统一改为环境事务真绑定：Mongo 环境会话（全部集合读写/CAS/序列走 session 重载；standalone 回退清会话后单次直通执行，消除双写）、PG 环境事务（操作上下文 SetDbConnection + UseTransaction 加入壳事务——EF 要求同连接；DDL 引导留事务外；EF SaveChanges 自动 savepoint 保住 23505 一次重试收敛）、SQLite 环境事务对象（八处自持事务写路径 BeginOrJoin——加入环境事务时只挂 command.Transaction 不再 BEGIN/COMMIT，TryAddUnique 冲突不再回滚环境事务）。嵌套事务壳三家一致加入环境事务；Mongo/PG 非重试失败仍包 DatabaseUnavailableException、SQLite 仍裸抛（既有契约冻结）；IDatabaseService/GameDb 公共签名零变化。新增跨 provider 原子性套件 DatabaseExecuteInTransactionAtomicityTests（提交全路径持久化/异常整体回滚含序列不推进与删除撤销/嵌套壳加入；Mongo standalone 运行时探测跳过回滚断言并断言回退恰执行一次）
* WorkerActor 超时不再破坏 Actor 串行不变量（P0-A#1 / C238）：`InnerRun` 超时放行等待方后邮箱继续等待孤儿任务完成，而非放任下一条消息并发进入同一 Actor（数据竞争根因）；孤儿超过硬上限（10×TimeOut）仍无结束迹象才隔离该 Actor——新增 `WorkerActor.IsQuarantined`，隔离后新入队与排队中工作单元以 InvalidOperationException 快速失败，需经宿主回收重建恢复。同批修复三类同源故障掩蔽/污染（等待方契约变更，消息泵 BaseMessageHandler 三层 catch 兜底不受影响）：超时改以 TimeoutException 传递等待方（原为 TrySetResult(false/default) 假成功，调用方拿到 null 当正常值用）；工作单元异常改传播给等待方（原吞异常后以 default(T) 假成功）；`WorkWrapper.ResetContext` 改条件重置（仅链 ID 仍归属本工作单元时清零），孤儿任务/被拒入队工作单元不再误清后继消息的链上下文（原会令重入调用误判入队自等待 → 级联超时）。另修复 Enqueue/SendAsync 调试限流路径 `return default`（null Task）在调用方 await 时必然 NRE 的问题，改为跳过语义的已完成任务
* Actor 工作单元执行期协作取消（P0-A#1 / C238）：新增 `RuntimeContext.CurrentCancellationToken`（AsyncLocal，随异步流流转，默认 CancellationToken.None）——WorkerActor 执行期为每个工作单元创建联动取消源（调用方令牌 + 超时 CancelAfter 合并），业务代码与 IO 层读取该令牌即可在超时或调用方取消时快速收尾，缩短孤儿任务占用邮箱的窗口；`Enqueue/SendAsync/Tell` 的 `cancellationToken` 参数由此同时具备「取消排队」与「取消执行」两层语义

* 退出钩子停机有界等待（C365 / GFX-1073 / GitHub #701）：`AppExitHandler` 的 `ProcessExit` 钩子此前触发停机回调后 fire-and-forget——启动异常退栈、game loop 自然完成或外部 `Environment.Exit` 后进程在停机流程（含日志刷盘）完成前退出，截断写操作；`Ctrl+C` / `SIGTERM` 路径仅靠入口正常路径尾部等待。修复三件——`AppEnter.HandleExit` 改返回停机任务（幂等触发返回既有任务作等待锚点，`Init` 回调升级 `Func<string, Task>`）；`ProcessExit` 钩子触发停机后经 `WaitForExitTaskCompletion`（`ShutdownWaitTimeout` 30 秒上限，超时记 Error 后放行）同步有界等待；`EntryAsync` 等待点移入 finally 有界等待已触发的停机任务，覆盖启动异常路径。新增本地化键 `StartUp.Application.ShutdownWaitTimeout`（中英）。回归固化 `AppExitHandlerShutdownWaitTests` 四例（幂等同任务引用 / 挂起任务有界返回 / 完成与失败任务直返不抛 / 启动异常路径入口返回前等待停机任务）。

### Performance
* 消息处理器注册期工厂化（C347，GFX-1056）：`HotfixModule.GetMessageHandler` 原先每条消息都 `Activator.CreateInstance` 反射创建处理器并做 `is IMessageHandler` 运行期类型判断（类型缺陷推迟到首条消息才爆出）；现 `_messageHandlerMap` 改存注册期构建的强类型工厂委托 `Func<IMessageHandler>`（复用 C338 的 `CreateFactory` / `CreateInstance` 与本地化键 `CoreExceptions.Hotfix.MissingParameterlessConstructor`），装载期即校验带 `[MessageMapping]` 的类型实现 `IMessageHandler` 且具有公共无参构造（违例抛 `InvalidOperationException` 带实现/服务双类型定位），`GetMessageHandler` 收敛为查表 + 委托调用且每次调用产出新实例（处理器有状态，不做单例/池化）；未命中消息号返回 null 行为不变。行为变化：不实现 `IMessageHandler` 或缺公共无参构造的 `[MessageMapping]` 类型由「首条消息时抛 `WrongMessageHandlerType`」改为「热更加载即失败」


* 入站消息处理移除每消息一次的 Task.Run 包装（C366 / GFX-1074 / GitHub #702）：`AppStartUpBase.InvokeMessageHandlerAsync` 将 `InitAsync`+`InnerActionAsync` 整体包进 `await Task.Run(...)`，唯一调用点 `PackageHandlerAsync` 本就 await 串行等待，包装只增加一次线程池排队跳数（高并发下加剧线程池压力）而无任何并行隔离收益；改为直接 await 两段调用（Init 失败早退与 `MessageHandlerError` 错误日志契约不变，`cancellationToken` 收敛为直接贯通 `InnerActionAsync`）。
* RemoteMessageClient 移除全局串行信号量，服务器间 RPC 全面并发化（P0-A#2 / C239）：原 `SemaphoreSlim(1,1)` 使 `CallAsync`/`SendOneWayAsync` 全进程串行（连接获取+响应等待全程持锁，多进程拓扑下所有服务器间 RPC 吞吐被锁死且无死锁保护）。新模型：每条连接一个专属接收泵（`DecodeAsync` 循环 → 请求-响应匹配器 `TryComplete` 路由）+ 每连接写入门控（同连接帧写入串行化、跨连接完全并行）；等待方经既有 `WaitResponseAsync`（此前为死代码）按 UniqueId 收响应，同一连接可承载任意多并发在途请求
* TcpConnectionProvider 单条 TCP 长连接改为按 endpoint 连接池（P0-A#3 / C239）：每 endpoint 独立成池（默认上限 4 条，构造参数可调），并发需求驱动懒建（顺序调用保持单连接复用，并发调用按需扩展），轮询分发；连接级故障仅精准移除该连接（`IConnectionProvider.Invalidate(Stream)`/`ITransportProtocolAdapter.Invalidate(Stream)` 增量接口），不再全量断连。顺带修复旧实现隐藏缺陷：全进程单连接槽位在多 endpoint 交替调用时每次断连重连
* 连接死亡快速失败（P0-A#2#3 / C239）：接收泵退出时经新增 `IRequestResponseMatcher.TryFail` 立即以 IOException 失败该连接上全部挂起请求（等待方收到可重试的 ConnectionFailed 而非干等超时），连接级异常（IO/Socket）仅失效当前连接、其余异常保守全量失效；新增本地化键 `RemoteMessaging.Client.ConnectionClosed`
* 新增 `RemoteMessageClientConcurrencyTests`（3 用例）：屏障式假服务端证明并发请求不再全局串行（旧实现必然全体超时）且扇出多连接；连接读后即断场景验证快速失败 + 池仅移除死亡连接后重建成功；双服务交替调用验证按 endpoint 连接复用（各 1 条连接，旧实现每次断连重连）

### Documentation

* 全仓禁用 `<inheritdoc />`（C301，GFX-1013）：55 个 `.cs` 文件 151 处 `/// <inheritdoc />`（含 2 处 `cref` 形态）替换为描述实现自身行为的完整双语 XML 文档注释（`<summary>` 中文、`<remarks>` 英文，`<param>` / `<returns>` / `<value>` / `<exception>` 用 `中文 / English`），覆盖 Capability 能力模块与 Options、Analyzers、Launcher 宿主适配、Templates 模板与测试替身；comment-hygiene 规范新增禁用条款。仅改 `///` 注释行，代码与行为零变更
## [1.13.7] - 2026-08-05

### Bug Fixes

* 将 FPMath.Epsilon 改为 readonly 消除 Sonar S2223 (#169)
## [1.13.6] - 2026-08-05

### Bug Fixes

* 将 FPMath.Deg2Rad 改为 readonly 消除 Sonar S2223 (#168)
## [1.13.5] - 2026-08-05

### Bug Fixes

* 将 FPMath.PiOver2 改为 readonly 消除 Sonar S2223 (#167)
## [1.13.4] - 2026-08-05

### Bug Fixes

* 将 FPMatrix4x4.InternalIdentity 改为 readonly 消除 Sonar S2223 (#164)
* 将 FPMath.Pi 改为 readonly 消除 Sonar S2223 (#165)
* 将 FPMath.Rad2Deg 改为 readonly 消除 Sonar S2223 (#166)
## [1.13.3] - 2026-08-05

### Bug Fixes

* 将 FPRandom.instance 收紧为 private 消除 Sonar S2223 (#163)
## [1.13.2] - 2026-08-05

### Bug Fixes

* 将 FPVector4.InternalZero 改为 readonly 消除 Sonar S2223 (#162)
## [1.13.1] - 2026-08-05

### build

* 忽略 codegraph 索引目录
## [1.13.0] - 2026-07-04

### Documentation

* 协议头注释改为 MIT 与 Apache 2.0 双许可证

### Features

* 新增通用房间协议
* 新增石头剪刀布游戏协议
* 精简好友内部协议字段

### build

* 升级 OpenTelemetry 与 Grafana 观测性依赖
* 升级 Aspire.Hosting 到 13.4.6
* 升级 Microsoft.CodeAnalysis 到 5.6.0
* 升级 Quartz 到 3.18.2
* 升级 Swashbuckle 到 10.2.3
* 升级 Mapster 到 10.0.10
* 升级测试项目 SDK 与 Roslyn 依赖
* 移除 Proto 空目录占位符
## [1.12.0] - 2026-07-04

### Bug Fixes

* 格式化调度日志时间

### Features

* 引入空事件参数兜底机制
## [1.11.0] - 2026-06-25

### Bug Fixes

* 修正异步泛型方法的 await 判定

### Features

* 新增 StateComponentAgent sealed 分析器
* 补充 StateComponentAgentSealedAnalyzer 单元测试
* 启用 ImplicitUsings 跟随全局默认

### build

* 移除 LangVersion 12 显式限制
* 清理与全局重复的编译属性声明
## [1.10.3] - 2026-06-14

### Bug Fixes

* 补启动流程缺失的 QuartzTimer.Start()

### Refactor

* QuartzTimer 内部改用 early return 减少嵌套
## [1.10.2] - 2026-06-11

### Styling

* 统一版权头为 Apache License 2.0 单协议
## [1.10.1] - 2026-06-11

### Documentation

* 补充版权持有人信息
* 统一许可证为 Apache 2.0

### build

* 完善 NuGet 包描述与标签
* 优化 WrapperGenerator 打包配置
* 禁止 AppHost 项目打包
## [1.10.0] - 2026-06-11

### Documentation

* 更新多语言 README 目录结构

### Features

* 新增 Architecture.Analyzers 架构分析器

### Miscellaneous Tasks

* 更新 .gitignore 和项目配置

### Refactor

* 删除旧版 CodeGenerator 项目文件
* 新增 Hotfix.WrapperGenerator 热更新包装器生成器
* 更新解决方案和项目引用
## [1.9.2] - 2026-06-10

### Refactor

* 提取 GameAppRuntime 管理应用运行状态
* StartupOptions 继承 AppSetting 并拆分日志配置
* 恢复 IsMonitorMessageTimeOut 属性名称

### Testing

* 适配 GameAppRuntime 重构并新增测试
## [1.9.1] - 2026-06-10

### Documentation

* 修正 README 缩进格式

### Refactor

* 迁移至 IIncrementalGenerator
## [1.9.0] - 2026-06-10

### Bug Fixes

* async void 改为 async Task
* 收紧 CORS 策略为显式白名单

### Documentation

* 明确仅支持 .NET 10

### Features

* 添加请求体和网络包大小限制
* 添加 HTTP 签名校验机制

### Refactor

* 重构 TimerSave 为批量处理模式并补充双语文档注释
* 消除 QuartzTimer 和 GlobalTimer 中的同步阻塞调用
* 重构退出流程为全异步并支持优雅关机
* 适配 Swashbuckle v10 和 OpenAPI 接口变更

### Testing

* 添加 WorkerActor / HTTP 请求体限制 / 远程消息编解码单元测试
* 添加签名校验和 CORS 配置测试
* 添加 WorkerActor 队列和顺序执行测试

### build

* 更新注释和 .gitignore
* 添加 SDK 版本和构建目标配置
* LangVersion 升级为 latest 并统一构建配置
* 升级全部 NuGet 包版本
* 升级 NuGet 包版本
## [1.8.1] - 2026-06-05

### Documentation

* 补充细粒度开关的 XML 文档注释
## [1.8.0] - 2026-06-04

### Features

* 添加 OpenTelemetry 可配置开关
* 支持 IsOpenTelemetryMetrics / IsOpenTelemetryTracing 细粒度开关
## [1.7.5] - 2026-05-23

### ci

* 添加镜像同步工作流
## [1.7.4] - 2026-05-23

### ci

* 迁移发布流水线到公共可复用工作流
## [1.7.3] - 2026-05-17

### Bug Fixes

* WorkerActor.CurrentChainId 线程安全实现
* 修复 OnlineSet 并发访问线程安全问题
* 修复无 AppHost 启动时 serverType 为空的兜底逻辑

### Documentation

* 重写 README 文档，更新项目架构和配置说明
* 同步中英文 README 并统一头部格式和动态版本徽章
* 添加繁體中文 README
* 添加日本語 README
* 添加 한국어 README

### Performance

* 添加机器人连接耗时追踪

### Refactor

* 重命名 README 文件，英文作为默认 README.md

### Styling

* 应用标准头部模板，添加居中布局和导航链接

### ci

* 更改触发分支从 src 到 main
* 更改触发分支从 src 到 main
## [1.7.2] - 2026-04-16

### Bug Fixes

* 移除对GameFrameX.NetWork.Kcp的未使用项目引用
## [1.7.1] - 2026-04-16

### Bug Fixes

* 移除KCP服务器启动代码并注释相关依赖
## [1.7.0] - 2026-04-16

### Bug Fixes

* 跨进程好友操作传递源玩家ID

### Features

* 添加好友关系状态实体
* 实现好友增删查业务逻辑和消息处理器
* 添加好友场景测试流程

### build

* 添加跨进程服务发现环境变量配置
* 机器人脚本支持 scenario 参数
## [1.6.1] - 2026-04-10

### Bug Fixes

* 修正跨天逻辑中driver角色计数与跨天天数计算
## [1.6.0] - 2026-04-09

### Bug Fixes

* 忽略StartupOptions.ServerType属性的空值检查
* 替换日志方法为控制台输出并调整执行顺序
* 移除冗余的缓存哈希相等警告日志
* 修复LogHelper.Error调用缺少泛型参数的问题
* 修复日志辅助方法调用时的泛型参数缺失问题
* 优化日志记录调用并调整应用退出流程
* 修正日志方法调用中的泛型参数
* 修正日志记录中泛型方法调用的类型参数
* 修复调试日志泛型调用错误
* 修复LogHelper.Error方法调用缺少泛型参数的问题
* 修复MongoDB索引一致性检查并添加异步关闭方法
* 修复SendAsync方法未实现和参数校验问题
* 修改minor_pattern以匹配feat前缀
* 将IsUseTimeZone默认值改为false
* 修复数据库连接失败时未释放服务实例的问题
* 在数据库初始化时添加时区设置选项
* 添加MongoDB健康检查的连续失败阈值
* 增强HTTP端口占用错误日志，显示占用进程详情
* 在WebSocket服务器启动失败时添加端口占用详情日志
* 在TCP服务器启动失败时增加端口占用详情日志
* 在KCP服务器启动失败时记录端口占用详情
* 修正全局ID计算以包含最大Actor类型值
* 修正泛型约束以使用 IResponseMessage 接口
* 修正目标框架属性名从复数到单数

### Documentation

* 为 Program.cs 添加双语文档注释
* 更新 ServerType 属性的注释和描述以更清晰
* 为MongoDB服务方法添加XML文档注释
* 为远程消息通信组件添加英文注释
* 为接口实现方法添加 XML 文档注释
* 为远程消息组件添加中文XML文档注释
* 为远程调用可观测性组件添加详细的中文注释
* 为远程消息组件添加XML注释的英文翻译
* 更新 README 添加多进程联调和机器人压测说明

### Features

* 支持单进程/多进程启动模式
* 为异步方法添加取消令牌支持并扩展功能
* 为MongoDB服务添加取消令牌支持和新增操作方法
* 添加GameDb的CRUD操作方法
* 添加数据库不可用异常类
* 增加连接重试机制和超时设置
* 增加数据库时区时间记录配置选项
* 添加带重试机制的读取和写入操作，优化MongoDB操作的稳定性
* 为MongoDbService添加异常消息的本地化支持
* 增强 MongoDB 连接与操作的监控和异常处理
* 添加数据库可用性状态枚举
* 添加数据库连接健康检查和自动恢复机制
* 添加 MongoDB 数据库健康检查支持
* 添加获取端口占用进程信息的功能
* 新增远程消息治理与调用编排统一客户端库
* 新增远程消息重试语义枚举类型
* 添加远程消息通信统一客户端框架
* 添加消息压缩配置支持
* 添加元数据支持和创建上下文工厂方法
* 添加远程调用便捷方法和单向发送支持
* 添加可扩展的消息压缩算法支持
* 引入网络发送器提供者模式以支持多种会话类型
* 引入可插拔传输协议适配器以支持多协议扩展
* 引入统一消息发送器基础设施
* 新增跨服转发处理器与默认路由组件
* 激活账号登录处理器并实现登录业务逻辑

### Miscellaneous Tasks

* 将 KCP 网络模块项目设置为不可打包
* 更新多个项目中的 GameFrameX.Foundation 包版本至 2.2.8
* 更新多个项目中的 GameFrameX.Foundation 包版本至 2.2.10
* 为多个文件添加开源许可证头部注释
* 替换 Visual Studio 解决方案文件为 slnx 格式
* 移除重复的版权声明并添加远程消息项目引用
* 移除重复的许可证和版权声明注释
* 添加缺失的项目引用以支持热更新和网络功能测试
* 更新服务ID常量并新增多个服务定义
* 移除硬编码环境变量并添加多个游戏服务实例

### Performance

* 优化MongoDB连接超时设置与连接复用
* 使用池化缓冲区优化消息编解码性能

### Refactor

* 重构启动选项类继承结构
* 合并 LauncherOptions 到 StartupOptions
* 简化 TopologyProfile 配置，移除默认值和预设模板
* 单进程模式强制要求显式指定 ServerType
* 移除 TopologyProfile，ServerType 统一支持单值和逗号分隔列表
* 移除 StopServerAsync 中的冗余日志刷新调用
* 为LogHelper方法调用添加泛型类型参数
* 将同步发送方法替换为异步发送
* 替换 MongoDB.Entities 为 MongoDB.Driver
* 统一使用 GetCurrentTimestamp 方法获取时间戳
* 移除 BaseCacheState 中的重复字段
* 将硬编码配置迁移至 DbOptions 并支持运行时调整
* 提取运行时配置到独立类以分离关注点
* 将表达式体属性转换为完整属性语法
* 简化 PooledBuffer 的内存管理逻辑
* 将 AlgorithmId 属性从表达式体改为标准 getter
* 移除网络通道中的 RPC 会话相关代码
* 移除未使用的WebSocketSession引用并简化DefaultNetWorkChannel构造
* 将Memory属性重构为完整属性语法
* 重新排序类成员并简化代码结构
* 移除接口文件中未使用的命名空间引用
* 重构服务常量到独立的类中
* 为服务常量类添加区域分组并重命名类
* 迁移业务调用点到统一消息入口
* 统一使用 GameServerConst 替代 GlobalConst 服务常量
* 移除 RemoteMessageClientHolder 全局入口
* 重构 Bot 客户端支持参数化配置和断线重连

### Styling

* 统一文件结尾格式并移除多余空行
* 移除接口实现方法上的多余inheritdoc注释
* 修复文件末尾缺少换行符的问题
* 移除未使用的 GameFrameX.NetWork.Message 全局引用

### Testing

* 添加 MongoDB 服务查询接口的集成测试
* 添加 MongoDB 服务查询的并发与边界条件测试
* 为MongoDB服务添加取消令牌及GameDb门面层测试
* 添加 MongoDB 服务连接与健康检查的单元测试
* 添加MongoDbService重试判定方法的单元测试
* 添加MongoDbService可用性状态机相关单元测试
* 添加远程消息模块的单元测试
* 新增统一消息系统单元测试

### build

* 升级到 .NET 10 并重构 docker-compose
* 添加 GameFrameX.NetWork.Abstractions 项目引用
* 更新 Dockerfile 和 docker-compose 配置
* 新增多服务部署配置和热更依赖同步
## [1.5.1] - 2026-03-24

### Bug Fixes

* 修复 HTTP 路由重复注册问题

### Features

* 添加基于 Aspire 的服务拓扑监控

### Refactor

* 移除 DiscoveryCenterManager 模块
* 更新启动逻辑以使用 Aspire 服务发现

### Testing

* 添加 HTTP 路由注册单元测试

### build

* 禁用非发布项目的 NuGet 打包
## [1.5.0] - 2026-03-24

### Bug Fixes

* 为 GetAgent 泛型方法添加 class 约束
* 添加 ThreadStatic 修复线程安全问题
* 修复代码缺陷和拼写错误
* 修复消息管道过滤器对零长度数据包的处理逻辑
* 修复健康检查标签数组语法错误
* 增强 MongoDB 服务的安全性和健壮性

### Documentation

* 将消息头长度注释从英文更新为中文并明确结构
* 翻译 Resources.zh-CN.resx 中的英文条目为中文
* 添加配置模块双语文档注释
* 添加工具模块双语文档注释
* 添加数学模块双语文档注释
* 更新双语文档注释
* 添加双语文档注释
* 添加双语文档注释
* 添加双语文档注释
* 添加双语文档注释
* 添加双语文档注释
* 添加双语文档注释

### Features

* 为 BaseCacheState 添加审计字段
* 添加 GET/PUT/DELETE 请求方法支持
* 添加HTTP请求日志开关配置
* 支持 URL 路由同时兼容驼峰和下划线格式
* 添加发现中心功能开关配置
* 添加 IsUseTimeZone 时区配置开关
* 添加本地化 key 检查和自动翻译脚本
* 补充缺失的本地化翻译
* 为HTTP服务器添加CORS支持
* 添加 KCP 网络模块
* 集成 KCP 服务器启动

### Miscellaneous Tasks

* 移除注释掉的 GetGenerateTime 方法
* 更新本地开发配置

### Performance

* 添加 SwaggerOperationFilter 反射缓存优化
* 避免重复 JSON 序列化/反序列化操作

### Refactor

* 迁移至新的 TimerHelper API
* 使用 RandomElement 替代 Random
* 重命名发布工作流文件
* 重构数据表 API 并优化性能
* 使用 TryGet 替代废弃的 Get 方法
* 使用 auto-property 替代 readonly 字段
* 移除不必要的zipFlag负数检查
* 使用sizeof计算消息头长度以提高可维护性
* 重命名 HttpStatusCode 为 GameHttpStatusCode 避免与系统类型冲突
* 代码清理和资源管理优化
* 简化本地化键引用路径以提升可维护性
* 简化本地化键路径的引用方式
* 简化本地化键的命名空间引用
* 简化本地化键的命名空间引用
* 简化本地化键的命名空间引用
* 将 switch 表达式重构为 switch 语句以提高可读性
* 提取 StartUpTypes 为独立的注册表类
* 拆分 Entry 函数为多个小函数
* 注释冗余的启动日志输出
* 优化 MongoDB 服务性能和异步安全
* 提取 ReplaceOptions 为静态字段
* 优化数据库模块代码质量
* 优化配置模块代码组织

### Styling

* 修复日志格式化字符串中的多余花括号
* 统一代码格式
* 为 SerializationException 添加 sealed 修饰符
* 规范静态字段命名
* 标准化计时器处理器文件的行尾和空格格式
* 统一文件换行符格式并调整条件编译块缩进
* 统一变量声明方式并添加访问修饰符
* 统一文件换行符格式为 LF
* 修复 Program.cs 文件中的换行符和缩进格式问题
* 统一接口文件格式并移除BOM头
* 统一文件编码和格式化代码
* 统一文件中的换行符格式
* 统一监控帮助类文件的换行符和末尾逗号格式

### Testing

* 更新测试以适配字段重命名

### build

* 升级 Foundation 包至 2.2.5 并适配大端序 API
* 添加 LangVersion 配置以支持 C# 12 功能
* 将 C# 语言版本从 12 降级到 10

### ci

* 将发布工作流触发分支改为 src
* 修复发布流水线分支推送和CHANGELOG配置
## [2.0.0.2-dev11] - 2026-02-24

### build

* 将项目目标框架从 net10.0 调整为支持 net10.0 和 net8.0
* 移除 .NET 8 目标框架，仅保留 .NET 10.0

### ci

* 将 .NET SDK 版本从 8.0 升级到 10.0.x
## [2.0.0.2-dev10] - 2026-02-24

### Bug Fixes

* 修复未初始化变量可能导致空引用异常的问题
* 修复GrafanaLoki标签已存在时的本地化键引用
* 修复压缩辅助类中日志记录异常信息的格式
* 统一非法词检测的日志格式
* 修复LNumber溢出错误日志中的本地化键名错误
* 修复日志记录中缺少参数占位符的问题
* 为警告日志添加统一的上下文前缀
* 修复响应消息错误码处理和日志记录格式
* 优化FormatMessage错误日志记录以包含更多上下文信息
* 修复日志记录异常信息不完整的问题
* 优化日志记录错误信息格式
* 修复日志记录异常时缺少描述信息的问题
* 将心跳消息列表从List改为HashSet以避免重复添加
* 为事件分发器扩展添加结构化日志记录
* 修复缓存哈希相等时未提前返回的问题
* 修复日志记录缺少关键参数的问题
* 改进日志输出格式以包含上下文信息
* 改进日志记录以包含更多上下文信息
* 修复日志本地化调用并优化错误码判断
* 改进日志记录，使用结构化参数并修复异常处理
* 修复消息编码中长度字段类型转换问题
* 移除消息编码中OperationType的冗余类型转换

### Documentation

* 更新README文档结构和内容
* Update README with detailed configuration guide and usage examples
* 更新过时警告信息为双语提示
* 在消息编码处理中添加注释说明

### Features

* 添加新的AppHost项目及相关配置
* 添加数据库密码配置选项
* 添加MongoDB支持并配置持久化容器
* 添加MongoDB健康检查实现
* 添加服务默认配置模块和OpenTelemetry支持
* 添加 GameFrameX.AppHost.ServiceDefaults 项目到解决方案
* 添加服务器时区配置支持
* 添加日志文件写入配置选项
* 增加时区时间记录配置选项

### Refactor

* 修改MongoDbContext继承并调整数据库初始化逻辑
* 重构项目文件配置，统一管理公共属性
* 移除自定义健康检查和OpenTelemetry扩展，使用ServiceDefaults替代
* 优化日志记录以使用结构化日志格式
* 简化本地化键引用并移除多余空行
* 将字符串拼接日志改为结构化日志记录
* 优化 MongoDB 服务日志格式以增强可读性
* 移除Entry方法中的多余空行
* 调整 TimeZone 属性在 LauncherOptions 中的声明位置
* 重构日志记录以使用结构化消息格式
* 移除本地化依赖并改进日志记录
* 优化日志信息格式，移除本地化调用
* 标记过时的 Get 方法为 [Obsolete]
* 重构 OpenTelemetry 配置以支持动态导出器

### build

* 更新基础依赖包版本至2.0.0.2
* 更新多个项目的依赖包版本至2.0.0.2
* 移除各项目中的TargetFramework并统一配置多目标框架
* 更新 MongoDB.Entities 依赖至 25.0.0 版本
* 将目标框架从多版本支持改为仅支持.NET 8.0
* 将目标框架从net8.0升级到net10.0
* 更新多个项目的 GameFrameX.Foundation 包版本至 2.2.1
* 更新多个项目的 GameFrameX.Foundation 包版本至 2.2.2
## [2.0.0.2-dev09] - 2025-11-24

### Bug Fixes

* 修复BaseCacheState全局过滤条件逻辑

### Refactor

* 统一将CreateTime重命名为CreatedTime及相关属性
## [2.0.0.2-dev08] - 2025-11-24

### Bug Fixes

* 修改默认查询表达式以包含null检查
## [2.0.0.2-dev07] - 2025-11-20

### Documentation

* 更新本地化键的注释描述为更详细的信息
* 为网络监控助手类添加摘要注释
* 移除BaseComponentAgent的冗余泛型参数注释

### Features

* 添加GameFrameX.Localization模块并集成到各项目
* 为启动模块添加多语言支持
* 为网络消息模块添加本地化支持
* 为HTTP模块添加多语言支持
* 为网络抽象模块添加重复ID错误消息的本地化支持
* 添加网络模块本地化支持
* 添加启动器模块的日志和错误消息本地化支持
* 添加DiscoveryCenterManager模块的本地化键和资源
* 扩展数据表功能并添加类型约束
* 添加核心模块日志和异常消息的本地化支持
* 添加会话管理和BSON类映射的本地化支持
* 添加代码生成器代理错误消息的本地化支持
* 添加 GameFrameX.Localization 项目到解决方案
* 添加程序集资源提供者注册以支持本地化
* 添加实用工具模块的本地化支持
* 将硬编码日志消息替换为本地化字符串
* 添加启动模块相关消息的本地化支持
* 实现HTTP模块的国际化支持
* 添加网络模块RPC超时和消息编码异常的本地化支持
* 添加玩家和服务上下线回调的本地化支持
* 为服务连接管理添加多语言支持

### Refactor

* 移除MetricsHelper类及其相关监控功能
* 删除未使用的CustomAudioType枚举
* 统一中文资源文件格式并移除冗余英文翻译
* 改进异常消息和参数验证
* 优化常量命名和本地化引用路径
* 移除GameFrameX.Serialize项目及其相关文件
* 清理无用引用并优化本地化字符串引用
* 重构缓存状态接口和删除标记
* 更新依赖版本并优化本地化引用路径

### Styling

* 移除多余空行并格式化代码

### build

* 更新 GameFrameX.Foundation 相关包版本至 1.7.1.10
* 更新多个项目的依赖包版本至1.7.1.11
## [2.0.0.2-dev06] - 2025-11-19

### Bug Fixes

* 添加空检查防止空引用异常
* 使用NetHelper验证并解析DiscoveryCenterHost配置
* 修复_serverMap赋值语法错误
* 将日志控制台输出默认值改为true

### Features

* 添加获取域名IPv4和IPv6地址的方法
* 为GameAppServiceClient添加ID并更新回调参数
* 添加解析主机名或IP地址到EndPoint的方法
* 添加游戏应用服务客户端及配置管理
* 在RespConnectServer中添加ServerInstanceId字段
* 添加服务连接管理功能实现
* 添加游戏启动配置项和标签名称
* 添加ProtoBuf序列化测试及对象池测试
* 添加版权声明并重构消息ID生成方式
* 添加社交服务启动类及发现中心注册功能
* 添加好友组件及相关请求处理器

### Performance

* 使用对象池优化消息对象创建

### Refactor

* 为回调方法添加实例ID参数以增强日志追踪
* 修改GameAppClient方法以支持id参数
* 移除未使用的RPC调用代码并清理命名空间

### build

* 添加 LangVersion 10 到项目配置
* 添加 MongoDB 数据库支持并移除内置 Proto 项目
* 更新多个项目的依赖包版本至1.7.1.8
## [2.0.0.2-dev05] - 2025-11-07

### Bug Fixes

* 修复消息发送时未设置MessageId的问题

### Features

* 添加游戏客户端连接配置参数
## [2.0.0.2-dev4] - 2025-11-05

### Bug Fixes

* 修复配置文件加载失败时的日志显示顺序
* 调整日志初始化顺序以避免潜在的空引用异常
* 改进MongoDB服务初始化失败时的错误处理
* 添加启动任务为空的检查并输出警告信息

### Refactor

* 将Close方法改为异步返回Task
## [2.0.0.2-dev3] - 2025-11-04

### Bug Fixes

* 修复日志标签设置问题并添加日志类型配置

### Refactor

* 优化日志标签名称的构建逻辑

### build

* 更新多个项目的依赖包版本至1.7.1.7
## [2.0.0.2-dev2] - 2025-11-04

### Bug Fixes

* 添加路径空值检查并优化日志输出
* 将LogOptions.Default.LogType改为LogTagName以更准确描述用途
* 修复启动逻辑错误并增强日志记录

### Documentation

* 添加WriteStateAsync方法的高消耗操作警告注释
* 完善服务客户端类和配置的文档注释
* 完善启动类和属性的文档注释
* 完善代码注释和文档说明
* 更新接口文档注释为双语格式

### Refactor

* 将定时任务相关代码拆分到单独文件
* 移除不必要的IComponentAgent接口继承
* 移除PlatformRuntimeHelper及其测试文件
* 统一使用LogHelper替代LogHelper.Console方法
* 统一GrafanaLoki用户名属性命名
* 移除未使用的日志类型拼接逻辑
* 优化服务器启动任务管理逻辑
* 将LogHelper.InfoConsole和LogHelper.DebugConsole替换为LogHelper.Info和LogHelper.Debug

### build

* 更新 GameFrameX.Foundation 相关包版本至 1.7.1.5
## [2.0.0.2-dev1] - 2025-10-31

### Bug Fixes

* 添加MessageId有效性检查防止无效消息发送

### Documentation

* 为Send和Call方法添加异常注释说明

### Features

* 添加HTTP服务启用开关配置
* 添加TCP服务开关配置
* 添加UDP支持配置及实现
* 扩展RPC会话功能并重构数据管理
* 添加游戏应用TCP服务客户端实现
* 添加发现中心组件调用方法

### Refactor

* 重构服务客户端配置为独立类并整合功能
* 重构游戏应用客户端为服务客户端并添加RPC支持
* 统一将_gameAppClient重命名为_gameAppServiceClient以保持命名一致性
## [2.0.0.1-dev9] - 2025-10-25

### Documentation

* 修正项目描述中的错误库名引用
* 更新RPC处理器返回类型注释为IMessageHandler
* 完善基础组件代理类的日志记录器文档

### Features

* 添加MongoDB数据库服务实现
* 添加 GameFrameX.DataBase.Mongo 项目到解决方案
* 添加ActorIdGenerator工具类用于生成Actor唯一ID

### Refactor

* 将EnvironmentHelper迁移至Foundation.Utility命名空间
* 将ConsoleHelper移动到Foundation.Utility命名空间
* 替换Yitter.IdGenerator为自定义雪花ID实现
* 将TimeHelper重命名为TimerHelper并统一引用
* 统一使用TimerHelper替换TimeHelper的时间方法
* 移除未使用的引用并添加MongoDB数据库项目引用
* 移除BaseComponentAgent中未使用的using和接口依赖
* 替换MongoIndexAttribute为EntityIndexAttribute
* 将CacheComponent的set访问器改为private
* 移除未使用的扩展方法及相关测试
* 优化MongoDB查询服务的命名空间引用
* 移除LoadFromDbPostHandler的冗余参数
* 移除 MongoDB.Entities 和 GameFrameX.Foundation.Hash 的包引用

### Testing

* 删除过时的TimeHelper测试文件

### build

* 更新多个项目的 GameFrameX.Foundation 包版本至 1.7.0.1
* 更新 GameFrameX.Foundation 相关包版本至 1.7.0.7
* 添加 GameFrameX.Apps 项目引用到测试项目
* 添加 GameFrameX ORM 相关依赖包
* 更新多个项目的依赖包版本至1.7.1
## [2.0.0.1-dev8] - 2025-10-20

### Bug Fixes

* 修复玩家登录查询时未禁用跟踪的问题
* 在登录流程中添加玩家ID绑定
* 修复消息对象池管理问题

### Features

* 为BaseComponentAgent添加日志功能支持

### Refactor

* 优化心跳消息处理并使用对象池

### Styling

* 调整控制台logo显示格式和内容
## [2.0.0.1-dev7] - 2025-10-14

### Bug Fixes

* 修复RPC消息处理超时未返回错误响应的问题
* 捕获异常时设置默认错误码

### Documentation

* 更新错误码的英文描述和注释

### Features

* 添加网络操作错误码枚举类
* 添加执行超时错误码
## [2.0.0.1-dev6] - 2025-10-13

### Refactor

* 移除sealed修饰符并修复代码格式
## [2.0.0.1-dev5] - 2025-10-13

### Bug Fixes

* 修正日志中消息类型显示错误的问题
* 在消息编码器中添加finally块释放消息对象

### Documentation

* 为基类处理器添加泛型参数文档注释
* 为基类处理器添加泛型参数和继承说明文档

### Features

* 添加 RPC 异常基类用于远程过程调用错误处理
* 添加基础消息处理器接口
* 添加IMessageHandler接口定义网络消息处理能力
* 添加获取RPC处理器的方法
* 添加玩家上下线通知功能
* 添加服务上下线日志输出
* 添加基础消息处理器和组件处理器基类
* 添加基础RPC消息处理器和组件处理器实现
* 添加全局RPC组件处理器基类
* 添加玩家RPC组件处理器基类
* 为IMessage接口添加Clear方法
* 添加Clear方法用于清除消息内容
* 添加支持合并模式的反序列化方法
* 添加消息对象池帮助类用于高性能对象重用
* 为消息类添加Clear方法实现

### Performance

* 使用对象池优化RPC消息处理性能
* 使用对象池优化心跳消息对象创建

### Refactor

* 更新消息操作类型命名并添加服务注销功能
* 使用全局常量替换硬编码的ServerId值
* 移除未使用的MessageCodeDescriptionAttribute类
* 重构登录处理逻辑和组件结构
* 重构玩家登录逻辑，移除冗余代码
* 删除未使用的PetComponentAgent类
* 重构全局组件处理器为泛型版本
* 重构玩家组件处理器为泛型版本
* 将消息处理器返回类型改为IMessageHandler接口
* 重构背包相关消息处理器为RPC风格
* 重构玩家登录处理逻辑使用RPC模式
* 添加抽象方法Clear用于清除消息内容
* 重构消息接口继承关系
* 使INetworkMessageHeader继承IMessageObject并添加Clear方法
* 删除内部消息对象头类 InnerMessageObjectHeader
* 移除未使用的InnerMessageObjectHeader并优化调试日志
* 移除未使用的Register方法
* 提取对象池创建逻辑到独立方法
* 合并属性注解中的DefaultValue到Option特性中

### build

* 更新 Lib.Harmony 依赖版本至 2.4.1
* 更新 GameFrameX.Foundation 相关依赖包至 1.6.0 版本
## [2.0.0.1-dev4] - 2025-10-10

### Bug Fixes

* 修正内部消息处理逻辑，仅处理消息ID小于0的内部消息
* 修复服务器类型日志显示问题

### Features

* 添加网络消息包接口及实现类

### Refactor

* 移除废弃的外部网络消息接口和实现类
* 移除内部网络消息接口及实现类
* 统一使用NetworkMessagePackage创建消息对象
* 将IInnerNetworkMessage重命名为INetworkMessagePackage
* 将OuterNetworkMessage替换为NetworkMessagePackage类型
## [2.0.0.1-dev3] - 2025-10-10

### Bug Fixes

* 修正服务器类型检查条件中的常量名称错误
* 移除ServerId的默认值以避免合服问题

### Refactor

* 统一服务名称格式并添加服务器ID常量
* 将GameName替换为GameServiceName以保持命名一致性
* 使用常量替换硬编码的服务名称和ID
* 移除已废弃的UnSchedule方法
* 移除已过时的UTC时间转换方法
* 移除可空类型标记以简化代码

### Testing

* 移除重复的时间转换测试用例
## [2.0.0.1-dev2] - 2025-10-10

### Bug Fixes

* 修正ReqServiceUnRegister消息类型处理器的消息ID
* 将服务器异常日志从Info级别改为Warning级别

### Documentation

* 完善类和方法注释的英文翻译
* 更新压缩解压缩辅助类的文档注释
* 完善环境帮助类的文档注释

### Refactor

* 将日志消息从中文翻译为英文
* 优化错误提示和参数验证逻辑
* 移除未使用的命名空间引用
* 将HTTP服务器相关日志和错误信息翻译为英文
* 将健康检查消息从中文改为英文
* 将日志和异常消息从中文改为英文
* 将错误信息和日志从中文改为英文
* 统一客户端连接和断开日志的英文格式
## [2.0.0.1-dev1] - 2025-09-29

### Bug Fixes

* 修复WebSocket服务器启动逻辑并改进错误处理
* 修复消息操作类型比较和枚举值转换问题
* 修复心跳消息处理器的类型转换问题

### Features

* 添加服务器ID和内部端口配置
* 添加心跳接口IActorHeartBeat用于标识具备心跳时间戳的Actor
* 添加INotifyActorHeartBeat接口用于处理心跳结果
* 新增服务下线通知接口IServiceNotifyOffline
* 添加服务上线通知接口定义
* 添加服务注册接口定义
* 添加内置服务注册相关消息类型
* 重命名连接服务器消息类型并添加相关协议
* 添加游戏服务相关的常量名称

### Refactor

* 移除ServerType枚举定义
* 将ServerType从枚举类型改为字符串类型
* 将ServerType从枚举类型改为字符串类型
* 使用全局常量替换硬编码的服务类型检查
* 将ServerType从枚举类型改为字符串类型
* 将ServerType改为string类型以增加灵活性
* 将ServerType从枚举类型改为字符串类型
* 将ServerType从枚举类型改为字符串类型
* 移除已废弃的ClientMessage编解码处理器
* 重构消息操作类型枚举并添加明确值
* 将MessageOperationType从枚举类型改为byte类型
* 移除未使用的服务器消息类
* 重命名玩家上下线消息类以统一命名规范
* 统一服务注册与通知的消息类型命名
* 使用全局常量替换硬编码的服务类型
* 将ServerType.Game替换为GlobalConst.GameName
## [2.0.0-dev4] - 2025-09-29

### Bug Fixes

* 修改HttpPort默认值为8080
* 为InnerPort属性添加默认值8888
* 为ServerId属性添加默认值2000
* 调整HTTP服务器端口范围下限至5000

### Features

* 添加 WebSocket 启用开关配置

### Refactor

* 更新启动配置环境变量
## [2.0.0-dev3] - 2025-09-29

### Bug Fixes

* 修复移除节点时使用的方法调用错误
* 修复服务发现中服务器实例ID的过滤条件
* 修复断开连接时会话移除方法调用错误
* 修复获取节点信息时使用错误的方法名
* 添加消息处理器异常捕获并记录致命错误
* 添加空会话检查防止空引用异常

### Features

* 添加游戏客户端心跳处理逻辑
* 添加客户端API和HubAPI地址配置项

### Refactor

* 重构服务信息接口和实现类
* 统一服务器信息字段命名
* 重构服务节点管理方法并改进注释
* 重命名TrySessionRemove为TryRemoveBySessionId以提高可读性
* 统一服务器地址字段命名从Ip改为Host
* 重命名API相关属性名以更准确表达用途
* 将IP地址属性重命名为主机地址属性
## [2.0.0-dev2] - 2025-09-29

### Bug Fixes

* 将发现中心服务器连接日志从Info级别调整为Debug级别
* 添加发现中心主机和端口的配置检查

### Features

* 重构心跳发送逻辑以支持自定义心跳消息

### Refactor

* 提取终结点变量提升代码可读性
* 移除未使用的命名空间引用以简化代码
* 将私有字段_mRetryCount改为公共属性RetryCount
* 将_maxRetryCount改为公共属性并移除_reqActorHeartBeat
* 移除未使用的GameFrameX.Proto.BuiltIn命名空间引用
* 将DiscoveryCenterIp重命名为DiscoveryCenterHost
* 重构 GameAppClient 配置参数为选项类
* 将返回null改为返回default以符合代码规范
* 移除GameFrameX.Proto.BuiltIn项目并迁移内置协议到GameFrameX.Proto
* 重构游戏应用客户端配置使用方式
* 简化GameAppClient初始化逻辑并移除多余空行
* 移除未使用的程序集引用并简化消息协议初始化
## [2.0.0-dev1] - 2025-09-28

### Bug Fixes

* 修复当actorId为默认值时日志格式错误的问题
* 将NotifyHeartBeat更改为NotifyActorHeartBeat以匹配协议
* 修复发送消息时服务器类型显示错误
* 修复服务器启动时传入空HTTP处理器列表的问题
* 将LogHelper.Warn改为LogHelper.Warning以保持命名一致性
* 在反序列化消息时设置消息ID和操作类型
* 修复会话为空时未处理的异常
* 修复消息编码时使用错误参数的问题
* 修复服务器启动时注册发现中心的逻辑错误
* 启用DiscoveryCenter的StartUpTag标记
* 添加Json序列化选项以使用驼峰命名
* 修复日志中服务器类型显示错误的问题
* 将网络消息日志级别从Info调整为Debug
* 修改节点添加时的空值检查逻辑
* 在发送心跳时更新唯一标识符

### Documentation

* 为测试文件添加版权声明和许可证信息
* 统一添加项目版权声明和许可证信息
* 统一添加项目版权声明和许可证信息
* 统一添加项目版权声明和许可证信息
* 统一添加项目版权声明和许可证信息
* 统一添加项目版权声明和许可证信息
* 统一添加项目版权声明和许可证信息
* 统一添加项目版权声明和许可证信息
* 统一添加项目版权声明和许可证信息
* 统一添加项目版权声明和许可证信息
* 统一添加项目版权声明和许可证信息
* 统一添加项目版权声明和许可证信息
* 统一添加项目版权声明和许可证信息
* 统一添加项目版权声明和许可证信息
* 统一添加项目版权声明和许可证信息
* 统一添加项目版权声明和许可证信息
* 更新选项描述以提供更清晰的运维信息

### Features

* 添加发现中心客户端功能实现
* 添加通过发现中心注册热更新游戏服务器的功能
* 添加消息头长度属性
* 添加支持指定版本的热更新模块加载方法
* 添加热更新支持并初始化组件注册
* 添加服务器列表HTTP处理接口
* 实现获取外部服务器列表的HTTP接口
* 添加获取所有节点列表的HTTP接口
* 添加根据服务器ID获取节点信息的HTTP处理器
* 添加获取节点总数的HTTP处理器
* 添加根据SessionId获取节点信息的HTTP处理器
* 添加获取自身服务信息的HTTP处理器
* 添加移除节点的HTTP处理器实现
* 添加根据服务器类型获取节点列表的HTTP处理器
* 添加内置协议消息类实现
* 添加玩家信息接口、实体类及管理器实现
* 添加玩家注册在线和离线消息类型
* 添加发现中心组件及相关状态和代理类
* 在热更新启动流程中添加发现中心组件注册
* 添加玩家上下线时向发现中心发送通知
* 添加服务器类型和ID到日志选项
* 添加根据启动选项设置日志标签名的功能

### Refactor

* 移除未使用的WebSocket和SuperSocket配置代码
* 移除未使用的网关连接相关代码
* 优化网络消息日志输出逻辑
* 移除未使用的命名空间引用
* 将消息头长度属性类型从int改为ushort和uint
* 标记客户端消息编解码处理器为过时
* 重构消息头解析逻辑以提高可读性
* 修改消息头长度属性类型为ushort
* 优化消息编码处理，使用ArrayPool替代对象池
* 移除未使用的GameFrameX.ProtoBuf.Net引用
* 移除IMessageEncoderHandler中的IInnerNetworkMessage接口方法
* 移除未使用的内部消息处理逻辑
* 使用默认消息编解码器替换客户端特定实现
* 重构NamingServiceManager以支持多服务实例
* 优化服务管理器的初始化和节点获取逻辑
* 更新HTTP处理器并统一添加版权声明
* 移除未使用的数据库和HTTP相关检查方法
* 移除LauncherOptionsValidate方法
* 移除内部参数检查方法
* 移除注释掉的性能监控代码以保持代码整洁
* 重构健康检查响应模型并使用强类型
* 重命名APMPort为MetricsPort并移除无用配置
* 使用Mapster映射健康检查设置
* 重构服务发现中心模块并重命名
* 重构服务发现中心模块的命名空间和玩家管理
* 重命名服务器变更回调变量名以简化
* 使用JsonHelper序列化节点信息并重命名事件
* 移除已注释的节点状态设置方法
* 清理未使用的消息操作类型并修复代码格式

### Testing

* 更新AppSettingTests测试文件中的格式和空白行

### build

* 添加Hotfix模块支持并优化容器配置
* 添加 DiscoveryCenterManager 项目到构建流程
## [1.5.1-dev37] - 2025-06-17

### [增加]

* 增加雪花ID的配置参数
* 增加雪花ID的参数的配置化
## [1.5.1-dev36] - 2025-06-17

### [增加]

* 增加数据存档的参数
* 增加存档参数的配置化
* 增加Actor的配置参数
* 增加Actor的超时参数的配置化
* 增加环境变量的参数打印
## [release-v1.0.0.0] - 2024-05-15
<!-- generated by git-cliff -->
