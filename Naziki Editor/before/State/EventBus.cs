using System;
using System.Collections.Generic;

namespace Naziki_Editor.State
{
    // ==========================================
    // 📻 全局校园广播站 (EventBus 解耦神器)
    // ==========================================
    public static class EventBus
    {
        // 这里存放着所有的“频道”和“订阅了这个频道的耳机(委托)”
        private static readonly Dictionary<string, List<Delegate>> _subscribers = new Dictionary<string, List<Delegate>>();

        // 🎧 戴上耳机监听频道 (带数据包)
        public static void Subscribe<T>(string eventName, Action<T> action)
        {
            if (!_subscribers.ContainsKey(eventName))
                _subscribers[eventName] = new List<Delegate>();
            _subscribers[eventName].Add(action);
        }

        // 🎧 戴上耳机监听频道 (不带数据包，只听个响)
        public static void Subscribe(string eventName, Action action)
        {
            if (!_subscribers.ContainsKey(eventName))
                _subscribers[eventName] = new List<Delegate>();
            _subscribers[eventName].Add(action);
        }

        // 📢 对着大喇叭喊话 (发送数据包)
        public static void Publish<T>(string eventName, T data)
        {
            if (_subscribers.ContainsKey(eventName))
            {
                foreach (var action in _subscribers[eventName])
                {
                    ((Action<T>)action)?.Invoke(data);
                }
            }
        }

        // 📢 对着大喇叭喊话 (不发数据，只发暗号)
        public static void Publish(string eventName)
        {
            if (_subscribers.ContainsKey(eventName))
            {
                foreach (var action in _subscribers[eventName])
                {
                    ((Action)action)?.Invoke();
                }
            }
        }
    }
}