// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.Core.Abstractions.Agent;
using GameFrameX.Core.Actors;
using GameFrameX.Foundation.Logger;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Network;
using GameFrameX.Network.Abstractions;
using Serilog;

namespace GameFrameX.Core.BaseHandler.RPC;

/// <summary>
/// 基础组件处理器基类
/// </summary>
/// <typeparam name="TRequest">请求消息类型，必须实现 <see cref="IRequestMessage"/> 接口，且具备无参构造函数</typeparam>
/// <typeparam name="TResponse">响应消息类型，必须实现 <see cref="IResponseMessage"/> 接口，且具备无参构造函数</typeparam>
/// <remarks>
/// 所有具体组件处理器均应继承自此抽象类，以实现统一的组件初始化、超时监控、性能统计与异常处理逻辑。
/// </remarks>
public abstract class BaseRpcComponentHandler<TRequest, TResponse> : BaseRpcMessageHandler<TRequest, TResponse> where TRequest : class, IRequestMessage, new() where TResponse : class, IResponseMessage, new()
{
    /// <summary>
    /// 组件代理ID
    /// </summary>
    protected long ActorId { get; set; }

    /// <summary>
    /// 组件代理类型
    /// </summary>
    protected abstract Type ComponentAgentType { get; }

    /// <summary>
    /// 缓存组件代理对象
    /// </summary>
    public IComponentAgent CacheComponent { get; private set; }

    /// <summary>
    /// 初始化
    /// </summary>
    /// <returns>初始化任务</returns>
    protected abstract Task<bool> InitActorAsync();

    /// <summary>
    /// 初始化
    /// </summary>
    /// <param name="message">网络消息</param>
    /// <param name="networkChannel">网络通道</param>
    /// <returns>返回是否初始化成功,true:成功,false:失败</returns>
    public override async Task<bool> InitAsync(INetworkMessage message, INetworkChannel networkChannel)
    {
        var initSuccess = await base.InitAsync(message, networkChannel);
        if (!initSuccess)
        {
            return false;
        }

        initSuccess = await InitActorAsync();
        if (!initSuccess)
        {
            return false;
        }

        if (CacheComponent == null)
        {
            if (ActorId == default)
            {
                // Localization: Core.MessageHandler.ActorIdIsZero - ActorId为0，无法获取组件，{0}，关闭通道
                LogHelper.Fatal(LocalizationService.GetString(Localization.Keys.Core.MessageHandler.ActorIdIsZero, message.GetType().FullName));
                NetworkChannel.Close();
                return false;
            }

            try
            {
                CacheComponent = await ActorManager.GetComponentAgentAsync(ActorId, ComponentAgentType);
            }
            catch (Exception e)
            {
                // Localization: Core.MessageHandler.RpcGetComponentFailed - BaseRpcComponentHandler.InitAsync 获取组件失败，关闭通道，actorId:{0}，组件代理类型:{1}，异常：\n{2}
                LogHelper.Fatal(e, LocalizationService.GetString(Localization.Keys.Core.MessageHandler.RpcGetComponentFailed, ActorId, ComponentAgentType.FullName, e));
                NetworkChannel.Close();
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 内部执行
    /// </summary>
    /// <param name="timeout">执行超时时间，单位毫秒，默认30秒</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>内部执行任务</returns>
    public override Task InnerActionAsync(int timeout = 30000, CancellationToken cancellationToken = default)
    {
        if (CacheComponent == null)
        {
            // Localization: Core.MessageHandler.RpcInnerActionCacheComponentNull - BaseRpcComponentHandler.InnerActionAsync 缓存组件为空，消息类型:{0}
            LogHelper.Fatal(LocalizationService.GetString(Localization.Keys.Core.MessageHandler.RpcInnerActionCacheComponentNull, RequestMessage.GetType().FullName));
            NetworkChannel.Close();
            return Task.CompletedTask;
        }

        CacheComponent.Tell(() => ExecuteWorkAsync(timeout, cancellationToken), timeout, cancellationToken);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 在组件代理上执行内部处理并回写响应（由 <see cref="InnerActionAsync"/> 调度）。
    /// 业务核心以 <see cref="Task.WaitAsync(TimeSpan, CancellationToken)"/> 包装：超时时向客户端回写独立的
    /// <see cref="OperationErrorCode.TimeOut"/> 响应（含 requestId），不再让客户端只能等自身超时。
    /// </summary>
    /// <param name="timeout">执行超时时间，单位毫秒</param>
    /// <param name="cancellationToken">取消令牌</param>
    private async Task ExecuteWorkAsync(int timeout, CancellationToken cancellationToken)
    {
        var response = MessageObjectPoolHelper.Get<TResponse>();
        var requestId = RequestMessage.UniqueId;
        Task coreTask = null;
        try
        {
            try
            {
                coreTask = InnerActionCoreAsync(RequestMessage, response, cancellationToken);
                await coreTask.WaitAsync(TimeSpan.FromMilliseconds(timeout), cancellationToken);
                response?.SetUniqueId(requestId);
            }
            catch (TimeoutException)
            {
                // Localization: Core.MessageHandler.RpcComponentExecutionTimeout - BaseRpcComponentHandler组件RPC执行超时，消息类型:{0} 超时:{1}ms
                LogHelper.Error(LocalizationService.GetString(Localization.Keys.Core.MessageHandler.RpcComponentExecutionTimeout, RequestMessage.GetType().FullName, timeout));

                // 超时响应使用独立池化实例：孤儿业务任务仍持有原响应，其写入不得污染已回写的超时响应
                var timeoutResponse = MessageObjectPoolHelper.Get<TResponse>();
                try
                {
                    timeoutResponse.ErrorCode = OperationErrorCode.TimeOut;
                    timeoutResponse.SetUniqueId(requestId);
                    if (NetworkChannel != null && !NetworkChannel.IsClosed())
                    {
                        await NetworkChannel.WriteAsync(timeoutResponse);
                    }
                }
                finally
                {
                    MessageObjectPoolHelper.Return(timeoutResponse);
                }

                // 原响应经终态延续归还，此处不再写回
                return;
            }
            catch (RpcException e)
            {
                if (response != null)
                {
                    response.ErrorCode = e.ErrorCode;
                }
            }
            catch (Exception e)
            {
                // Localization: Core.MessageHandler.InnerActionError - 内部操作错误: {0} {1}
                LogHelper.Error(e, LocalizationService.GetString(Localization.Keys.Core.MessageHandler.InnerActionError, RequestMessage.GetType().FullName, RequestMessage));
                if (response != null && response.ErrorCode == 0)
                {
                    response.ErrorCode = OperationErrorCode.InternalServerError;
                }
            }

            // 取消等路径下业务核心仍在运行（孤儿持有 response 引用）：写共享实例会被并发改写撕裂，
            // 该响应无人等待时不写回（超时路径已用独立实例回写过 TimeOut）
            if (coreTask != null && !coreTask.IsCompleted)
            {
                return;
            }

            if (NetworkChannel == null || NetworkChannel.IsClosed() || response == null)
            {
                return;
            }

            await NetworkChannel.WriteAsync(response);
        }
        finally
        {
            // 任意路径（含通道关闭早退、写通道异常）均归还响应对象，避免对象池耗尽；
            // 超时孤儿业务任务未到终态时，经终态延续归还（对齐 BaseRpcMessageHandler 模式）
            if (coreTask == null || coreTask.IsCompleted)
            {
                if (response != null)
                {
                    MessageObjectPoolHelper.Return(response);
                }
            }
            else
            {
                _ = coreTask.ContinueWith((completedTask, state) =>
                {
                    _ = completedTask.Exception;
                    var orphanResponse = (TResponse)state;
                    if (orphanResponse != null)
                    {
                        MessageObjectPoolHelper.Return(orphanResponse);
                    }
                }, response, CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }

    /// <summary>
    /// 根据组件类型获取对应的 IComponentAgent
    /// </summary>
    /// <typeparam name="TOtherAgent">组件代理类型</typeparam>
    /// <returns>组件代理任务</returns>
    protected Task<TOtherAgent> GetComponentAgentAsync<TOtherAgent>() where TOtherAgent : IComponentAgent
    {
        if (CacheComponent == null)
        {
            // Localization: Core.MessageHandler.RpcGetComponentAgentCacheComponentNull - BaseRpcComponentHandler.GetComponentAgentAsync 缓存组件为空，消息类型:{0}
            var errorMessage = LocalizationService.GetString(Localization.Keys.Core.MessageHandler.RpcGetComponentAgentCacheComponentNull, RequestMessage.GetType().FullName);
            NetworkChannel.Close();
            throw new InvalidOperationException(errorMessage);
        }

        return CacheComponent.GetComponentAgentAsync<TOtherAgent>();
    }
}