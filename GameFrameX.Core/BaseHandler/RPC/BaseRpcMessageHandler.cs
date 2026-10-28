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

using System.Diagnostics;
using GameFrameX.Foundation.Logger;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Network;
using GameFrameX.Network.Abstractions;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Core.BaseHandler.RPC;

/// <summary>
/// 基础消息处理器
/// </summary>
/// <typeparam name="TRequest">请求消息类型，必须实现 <see cref="IRequestMessage"/> 接口，且具备无参构造函数</typeparam>
/// <typeparam name="TResponse">响应消息类型，必须实现 <see cref="IResponseMessage"/> 接口，且具备无参构造函数</typeparam>
/// <remarks>
/// 所有具体消息处理器均应继承自此抽象类，以实现统一的消息初始化、超时监控、性能统计与异常处理逻辑。
/// </remarks>
public abstract class BaseRpcMessageHandler<TRequest, TResponse> : IMessageHandler where TRequest : class, IRequestMessage, new() where TResponse : class, IResponseMessage, new()
{
    private bool _isInit;

    /// <summary>
    /// 监控器
    /// </summary>
    private readonly Stopwatch _stopwatch = new();

    /// <summary>
    /// 网络频道
    /// </summary>
    public INetworkChannel NetworkChannel { get; private set; }

    /// <summary>
    /// 消息对象
    /// </summary>
    protected TRequest RequestMessage { get; private set; }

    /// <summary>
    /// 初始化
    /// 子类实现必须调用
    /// </summary>
    /// <param name="message">消息对象</param>
    /// <param name="networkChannel">网络渠道</param>
    /// <returns>返回是否初始化成功,true:成功,false:失败</returns>
    public virtual Task<bool> InitAsync(INetworkMessage message, INetworkChannel networkChannel)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(networkChannel);
        if (message is not TRequest requestMessage)
        {
            // Localization: CoreExceptions.Message.TypeCastError - 消息类型错误, {0} to: {1}
            throw new InvalidCastException(LocalizationService.GetString(Localization.Keys.CoreExceptions.Message.TypeCastError, message.GetType().FullName, typeof(TRequest).FullName));
        }

        RequestMessage = requestMessage;
        NetworkChannel = networkChannel;
        _isInit = true;

        return Task.FromResult(true);
    }

    /// <summary>
    /// 执行
    /// </summary>
    /// <remarks>
    /// 响应对象从消息对象池借出：业务任务到达终态（成功/失败/已取消）后归还对象池；
    /// 超时或外部取消时先经联动取消源取消业务任务，仍在运行的孤儿任务所持有的响应对象不归还，防止其写入污染下一个借用者。
    /// </remarks>
    /// <param name="timeout">执行超时时间，单位毫秒，默认30秒</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>执行任务</returns>
    public virtual async Task InnerActionAsync(int timeout = 30000, CancellationToken cancellationToken = default)
    {
        if (_isInit == false)
        {
            // Localization: CoreExceptions.Message.HandlerNotInitialized - 消息处理器未初始化,请调用先Init方法，如果已经子类实现了Init方法，请调用在子类Init中调用父类Init方法
            throw new Exception(LocalizationService.GetString(Localization.Keys.CoreExceptions.Message.HandlerNotInitialized));
        }

        TResponse response = null;
        Task coreTask = null;
        // 与外部取消令牌联动的取消源：超时或外部取消时取消业务任务，阻止孤儿任务继续写入 response
        var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            response = MessageObjectPoolHelper.Get<TResponse>();
            var requestId = RequestMessage.UniqueId;

            if (NetworkChannel == null || NetworkChannel.IsClosed() || response == null)
            {
                return;
            }

            coreTask = InnerActionCoreAsync(RequestMessage, response, linkedCancellationTokenSource.Token);
            try
            {
                // 先等待业务逻辑执行完成（包括超时检查）/ Wait for business logic to complete (including timeout check)
                await coreTask.WaitAsync(TimeSpan.FromMilliseconds(timeout), cancellationToken);

                // 业务逻辑执行成功后，设置响应ID并发送 / After successful execution, set response ID and send
                response.SetUniqueId(requestId);
                await NetworkChannel.WriteAsync(response);
            }
            catch (TimeoutException timeoutException)
            {
                // 超时后先取消仍在运行的业务任务，阻止其继续写入 response / Cancel the still-running business task to stop further writes to response
                linkedCancellationTokenSource.Cancel();

                // Localization: Core.MessageHandler.RpcExecutionTimeout - BaseRpcMessageHandler.InnerActionAsync 请求消息:{0} 消息: 执行超时:{1}
                LogHelper.Fatal(LocalizationService.GetString(Localization.Keys.Core.MessageHandler.RpcExecutionTimeout, RequestMessage, timeoutException.Message));

                // 超时响应使用独立实例，业务任务可能忽略取消并继续写入原响应。
                var timeoutResponse = MessageObjectPoolHelper.Get<TResponse>();
                try
                {
                    timeoutResponse.ErrorCode = OperationErrorCode.TimeOut;
                    timeoutResponse.SetUniqueId(requestId);
                    await NetworkChannel.WriteAsync(timeoutResponse);
                }
                finally
                {
                    MessageObjectPoolHelper.Return(timeoutResponse);
                }
            }
        }
        catch (Exception e)
        {
            var fullName = GetType().FullName;
            // Localization: Core.MessageHandler.RpcInnerActionException - BaseRpcMessageHandler.InnerActionAsync 处理器:{0} 请求消息:{1} 异常:{2}
            LogHelper.Fatal(LocalizationService.GetString(Localization.Keys.Core.MessageHandler.RpcInnerActionException, fullName, RequestMessage, e));
        }
        finally
        {
            if (coreTask == null || coreTask.IsCompleted)
            {
                // 业务任务已到达终态（成功/失败/已取消）或从未启动：response 无人再持有，释放联动取消源并安全归还对象池
                linkedCancellationTokenSource.Dispose();
                if (response != null)
                {
                    MessageObjectPoolHelper.Return(response);
                }
            }
            else
            {
                // 业务尚未结束时保留其响应与取消源；终态后再释放，避免提前复用和外部令牌注册泄漏。
                _ = coreTask.ContinueWith((completedTask, state) =>
                {
                    var cleanup = (Tuple<CancellationTokenSource, TResponse>)state;
                    _ = completedTask.Exception;
                    cleanup.Item1.Dispose();
                    if (cleanup.Item2 != null)
                    {
                        MessageObjectPoolHelper.Return(cleanup.Item2);
                    }
                }, Tuple.Create(linkedCancellationTokenSource, response), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }

    /// <summary>
    /// 动作异步
    /// </summary>
    /// <param name="request">请求消息</param>
    /// <param name="response">响应消息</param>
    /// <param name="cancellationToken">取消令牌，超时或外部取消时触发，业务实现应在协作式取消点检查并停止写入响应</param>
    /// <returns>动作执行任务</returns>
    protected abstract Task ActionAsync(TRequest request, TResponse response, CancellationToken cancellationToken);

    /// <summary>
    /// 内部动作异步
    /// 记录执行时间并调用 <see cref="ActionAsync" />
    /// </summary>
    /// <param name="request">请求消息</param>
    /// <param name="response">响应消息</param>
    /// <param name="cancellationToken">取消令牌，透传给 <see cref="ActionAsync"/></param>
    /// <returns>动作执行任务</returns>
    internal async Task InnerActionCoreAsync(TRequest request, TResponse response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        if (GlobalSettings.CurrentSetting.IsMonitorMessageTimeOut)
        {
            _stopwatch.Restart();
            await ActionAsync(request, response, cancellationToken);
            _stopwatch.Stop();
            if (_stopwatch.Elapsed.TotalSeconds >= GlobalSettings.CurrentSetting.MonitorMessageTimeOutSeconds)
            {
                var fullName = GetType().FullName;
                // Localization: Core.MessageHandler.RpcExecutionTimeWarning - BaseRpcMessageHandler.InnerActionCoreAsync 处理器:{0} 请求消息:{1} 消息: 消息处理器：{2},UniqueId：{3} 执行耗时：{4} ms
                LogHelper.Warning(LocalizationService.GetString(Localization.Keys.Core.MessageHandler.RpcExecutionTimeWarning, fullName, RequestMessage, GetType().Name, RequestMessage.UniqueId, _stopwatch.ElapsedMilliseconds));
            }

            return;
        }

        if (GlobalSettings.CurrentSetting.IsDebug)
        {
            _stopwatch.Restart();
            await ActionAsync(request, response, cancellationToken);
            _stopwatch.Stop();
            var fullName = GetType().FullName;
            // Localization: Core.MessageHandler.RpcExecutionTimeDebug - BaseRpcMessageHandler.InnerActionCoreAsync 处理器:{0} 请求消息:{1} 消息: 消息处理器：{2},UniqueId：{3} 执行耗时：{4} ms
            LogHelper.Debug(LocalizationService.GetString(Localization.Keys.Core.MessageHandler.RpcExecutionTimeDebug, fullName, RequestMessage, fullName, RequestMessage.UniqueId, _stopwatch.ElapsedMilliseconds));
            return;
        }

        await ActionAsync(request, response, cancellationToken);
    }

    /// <summary>
    /// 获取当前消息处理器所处理的消息类型。
    /// 通常用于反射或泛型约束，便于框架自动识别和分发消息。
    /// </summary>
    /// <returns>消息类型的 <see cref="Type"/> 实例。</returns>
    public Type GetMessageType()
    {
        return typeof(TRequest);
    }

    /// <summary>
    /// 获取当前消息处理器返回的响应类型。
    /// 用于确定消息处理完成后返回的数据类型，便于类型安全和自动化处理。
    /// </summary>
    /// <returns>响应类型的 <see cref="Type"/> 实例。</returns>
    public Type GetResponseType()
    {
        return typeof(TResponse);
    }
}