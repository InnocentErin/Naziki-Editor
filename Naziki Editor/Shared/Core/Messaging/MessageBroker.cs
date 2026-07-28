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
        private readonly object _syncRoot = new();

        /// <summary>
        /// 全局默认消息代理实例，用于在尚未接入依赖注入前提供全局访问点。
        /// </summary>
        public static IMessageBroker Default { get; } = new MessageBroker();

        public IDisposable Subscribe<T>(string topic, Action<T> handler)
        {
            if (string.IsNullOrEmpty(topic)) throw new ArgumentException("主题不能为空", nameof(topic));
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            lock (_syncRoot)
            {
                if (!_subscribers.ContainsKey(topic))
                    _subscribers[topic] = new List<Delegate>();
                _subscribers[topic].Add(handler);
            }
            return new Subscription(() => Unsubscribe(topic, handler));
        }

        public IDisposable Subscribe(string topic, Action handler)
        {
            if (string.IsNullOrEmpty(topic)) throw new ArgumentException("主题不能为空", nameof(topic));
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            lock (_syncRoot)
            {
                if (!_subscribers.ContainsKey(topic))
                    _subscribers[topic] = new List<Delegate>();
                _subscribers[topic].Add(handler);
            }
            return new Subscription(() => Unsubscribe(topic, handler));
        }

        public void Publish<T>(string topic, T data)
        {
            if (string.IsNullOrEmpty(topic)) throw new ArgumentException("主题不能为空", nameof(topic));
            Delegate[] handlers;
            lock (_syncRoot)
            {
                if (!_subscribers.TryGetValue(topic, out var subscribers)) return;
                handlers = subscribers.ToArray();
            }
            foreach (var action in handlers)
                ((Action<T>)action)?.Invoke(data);
        }

        public void Publish(string topic)
        {
            if (string.IsNullOrEmpty(topic)) throw new ArgumentException("主题不能为空", nameof(topic));
            Delegate[] handlers;
            lock (_syncRoot)
            {
                if (!_subscribers.TryGetValue(topic, out var subscribers)) return;
                handlers = subscribers.ToArray();
            }
            foreach (var action in handlers)
                ((Action)action)?.Invoke();
        }

        private void Unsubscribe(string topic, Delegate handler)
        {
            lock (_syncRoot)
            {
                if (!_subscribers.TryGetValue(topic, out var subscribers)) return;
                subscribers.Remove(handler);
                if (subscribers.Count == 0) _subscribers.Remove(topic);
            }
        }

        private sealed class Subscription : IDisposable
        {
            private Action? _dispose;
            public Subscription(Action dispose) => _dispose = dispose;
            public void Dispose() => System.Threading.Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }
}
