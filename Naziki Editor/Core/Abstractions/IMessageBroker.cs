using System;

namespace Naziki_Editor.Core.Abstractions
{
    /// <summary>
    /// 消息代理抽象，提供基于主题（topic）的发布/订阅机制。
    /// 不包含任何 UI 依赖。
    /// </summary>
    public interface IMessageBroker
    {
        /// <summary>
        /// 订阅带数据包的主题。
        /// </summary>
        void Subscribe<T>(string topic, Action<T> handler);

        /// <summary>
        /// 订阅无数据包的主题。
        /// </summary>
        void Subscribe(string topic, Action handler);

        /// <summary>
        /// 向指定主题发布带数据包的消息。
        /// </summary>
        void Publish<T>(string topic, T data);

        /// <summary>
        /// 向指定主题发布无数据包的消息。
        /// </summary>
        void Publish(string topic);
    }
}
