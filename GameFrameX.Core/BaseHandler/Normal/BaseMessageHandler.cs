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
using GameFrameX.Core.Abstractions;
using GameFrameX.Foundation.Logger;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Network.Abstractions;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Core.BaseHandler.Normal;

/// <summary>
/// 基础消息处理器
/// </summary>
/// <typeparam name="TRequest">请求消息类型，必须实现 <see cref="IRequestMessage"/> 接口，且具备无参构造函数</typeparam>
/// <remarks>
/// 所有具体消息处理器均应继承自此抽象类，以实现统一的消息初始化、超时监控、性能统计与异常处理逻辑。
/// </remarks>
public abstract class BaseMessageHandler<TRequest> : IMessageHandler where TRequest : class, IRequestMessage, new()
{
    private bool _isInit;

    /// <summary>
    /// 监控器
    /// </summary>
    private Stopwatch _stopwatch;

    /// <summary>
    /// 网络频道
    /// </summary>
    public INetworkChannel NetworkChannel { get; private set; }

    /// <summary>
    /// 本次消息执行期的取消令牌字段。仅非组件处理器路径由 <see cref="InnerActionAsync"/> 赋值；
    /// 存令牌（值类型）而非取消源，使孤儿任务在取消源释放后仍可安全读取。
    /// </summary>
    /// <remarks>
    /// The cancellation token field for the current message execution. Only assigned by <see cref="InnerActionAsync"/> on the non-component handler path;
    /// stores the token (a value type) instead of the source so orphan tasks can still read it safely after the source is disposed.
    /// </remarks>
    private CancellationToken _executionCancellationToken;

    /// <summary>
    /// 本次消息执行期的取消令牌。超时或调用方取消时触发，业务代码应在耗时操作处传入或检查。
    /// 组件处理器在 actor 内执行且不经基类 <see cref="InnerActionAsync"/> 赋值，此时返回
    /// <see cref="RuntimeContext.CurrentCancellationToken"/>（即 WorkWrapper 写入的执行期令牌）。
    /// </summary>
    /// <remarks>
    /// The cancellation token for the current message execution. It fires on timeout or caller cancellation; business code should pass it into or check it at long-running operations.
    /// Component handlers execute inside an actor without assigning via the base <see cref="InnerActionAsync"/>, in which case this falls back to
    /// <see cref="RuntimeContext.CurrentCancellationToken"/> (the execution token written by WorkWrapper).
    /// </remarks>
    protected CancellationToken ExecutionCancellationToken
    {
        get
        {
            if (_executionCancellationToken.CanBeCanceled)
            {
                return _executionCancellationToken;
            }

            return RuntimeContext.CurrentCancellationToken;
        }
    }

    /// <summary>
    /// 消息对象
    /// </summary>
    public TRequest Message { get; private set; }

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

        _stopwatch = new Stopwatch();
        Message = requestMessage;
        NetworkChannel = networkChannel;
        _isInit = true;
        return Task.FromResult(true);
    }

    /// <summary>
    /// 执行
    /// </summary>
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

        var executionCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _executionCancellationToken = executionCancellationTokenSource.Token;
        Task task = null;
        try
        {
            task = InnerActionCoreAsync(Message);
            try
            {
                await task.WaitAsync(TimeSpan.FromMilliseconds(timeout), cancellationToken);
            }
            catch (TimeoutException timeoutException)
            {
                // 协作式取消：通知仍在运行的业务代码尽快收尾。先于日志执行，防日志通道异常跳过取消
                executionCancellationTokenSource.Cancel();
                // Localization: Core.MessageHandler.InnerActionTimeout - BaseMessageHandler.InnerActionAsync 执行超时，消息类型:{0}，超时:{1}，异常:{2}
                LogHelper.Fatal(LocalizationService.GetString(Localization.Keys.Core.MessageHandler.InnerActionTimeout, Message.GetType().FullName, timeout, timeoutException));
            }
            catch (Exception exception)
            {
                // Localization: Core.MessageHandler.InnerActionException - BaseMessageHandler.InnerActionAsync 执行异常，消息类型:{0}，异常:{1}
                LogHelper.Fatal(LocalizationService.GetString(Localization.Keys.Core.MessageHandler.InnerActionException, Message.GetType().FullName, exception));
            }
        }
        catch (Exception e)
        {
            LogHelper.Fatal(e);
        }
        finally
        {
            // 与 WorkWrapper.EndExecution 同口径：孤儿任务未结束时可能仍持有令牌注册，不释放取消源
            if (task == null || task.IsCompleted)
            {
                executionCancellationTokenSource.Dispose();
            }
        }
    }

    /// <summary>
    /// 动作异步
    /// </summary>
    /// <param name="request"></param>
    /// <returns>动作执行任务</returns>
    protected abstract Task ActionAsync(TRequest request);

    /// <summary>
    /// 内部动作异步
    /// 记录执行时间并调用 <see cref="ActionAsync" />
    /// </summary>
    /// <param name="message"></param>
    /// <returns>动作执行任务</returns>
    protected async Task InnerActionCoreAsync(TRequest message)
    {
        if (GlobalSettings.CurrentSetting.IsMonitorMessageTimeOut)
        {
            _stopwatch.Restart();
            await ActionAsync(message);
            _stopwatch.Stop();
            if (_stopwatch.Elapsed.TotalSeconds >= GlobalSettings.CurrentSetting.MonitorMessageTimeOutSeconds)
            {
                // Localization: Core.MessageHandler.ExecutionTimeWarning - 消息处理器：{0},UniqueId：{1} 执行耗时：{2} ms
                LogHelper.Warning(LocalizationService.GetString(Localization.Keys.Core.MessageHandler.ExecutionTimeWarning, GetType().Name, Message.UniqueId, _stopwatch.ElapsedMilliseconds));
            }

            return;
        }

        if (GlobalSettings.CurrentSetting.IsDebug)
        {
            _stopwatch.Restart();
            await ActionAsync(message);
            _stopwatch.Stop();
            // Localization: Core.MessageHandler.ExecutionTimeDebug - 消息处理器：{0},UniqueId：{1} 执行耗时：{2} ms
            LogHelper.Debug(LocalizationService.GetString(Localization.Keys.Core.MessageHandler.ExecutionTimeDebug, GetType().Name, Message.UniqueId, _stopwatch.ElapsedMilliseconds));
            return;
        }

        await ActionAsync(message);
    }

    /// <summary>
    /// 获取当前消息处理器所处理的消息类型。
    /// 通常用于反射或泛型约束，便于框架自动识别和分发消息。
    /// </summary>
    /// <returns>消息类型的 <see cref="Type"/> 实例。</returns>
    public Type GetMessageType()
    {
        return Message.GetType();
    }

    /// <summary>
    /// 获取当前消息处理器返回的响应类型。
    /// 用于确定消息处理完成后返回的数据类型，便于类型安全和自动化处理。
    /// </summary>
    /// <returns>响应类型的 <see cref="Type"/> 实例。</returns>
    public Type GetResponseType()
    {
        return default;
    }
}