using System;
using System.Collections.Generic;
using Naziki_Editor.Core.Abstractions;

namespace Naziki_Editor.Core.Messaging
{
    /// <summary>
    /// 消息代理实现，提供基于主题的发布/订阅机制。
    /// </summary>
    public class MessageBroker : IMessageBroker
    {
        private readonly Dictionary<string, List<Delegate>> _subscribers = new Dictionary<string, List<Delegate>>();

        /// <summary>
        /// 全局默认消息代理实例，用于在尚未接入依赖注入前提供全局访问点。
        /// </summary>
        public static IMessageBroker Default { get; } = new MessageBroker();

        public void Subscribe<T>(string topic, Action<T> handler)
        {
            if (string.IsNullOrEmpty(topic)) throw new ArgumentException("主题不能为空", nameof(topic));
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            if (!_subscribers.ContainsKey(topic))
                _subscribers[topic] = new List<Delegate>();
            _subscribers[topic].Add(handler);
        }

        public void Subscribe(string topic, Action handler)
        {
            if (string.IsNullOrEmpty(topic)) throw new ArgumentException("主题不能为空", nameof(topic));
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            if (!_subscribers.ContainsKey(topic))
                _subscribers[topic] = new List<Delegate>();
            _subscribers[topic].Add(handler);
        }

        public void Publish<T>(string topic, T data)
        {
            if (string.IsNullOrEmpty(topic)) throw new ArgumentException("主题不能为空", nameof(topic));
            if (!_subscribers.ContainsKey(topic)) return;

            foreach (var action in _subscribers[topic])
            {
                ((Action<T>)action)?.Invoke(data);
            }
        }

        public void Publish(string topic)
        {
            if (string.IsNullOrEmpty(topic)) throw new ArgumentException("主题不能为空", nameof(topic));
            if (!_subscribers.ContainsKey(topic)) return;

            foreach (var action in _subscribers[topic])
            {
                ((Action)action)?.Invoke();
            }
        }
    }
}
