using System;
using System.Collections;
using System.Collections.Generic;

namespace RxFSM
{
    public static class Disposable
    {
        public static IDisposable Create(Action onDispose) => new ActionDisposable(onDispose);
        public static readonly IDisposable Empty = new EmptyDisposable();

        private sealed class ActionDisposable : IDisposable
        {
            private Action _action;
            public ActionDisposable(Action action) => _action = action;
            public void Dispose()
            {
                var action = System.Threading.Interlocked.Exchange(ref _action, null);
                action?.Invoke();
            }
        }

        private sealed class EmptyDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    public sealed class CompositeDisposable : IDisposable, ICollection<IDisposable>
    {
        private readonly List<IDisposable> _list = new List<IDisposable>();
        private bool _disposed;

        public int Count => _list.Count;
        public bool IsReadOnly => false;

        public void Add(IDisposable d)
        {
            if (_disposed)
            {
                d?.Dispose();
                return;
            }
            _list.Add(d);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var d in _list) d?.Dispose();
            _list.Clear();
        }

        public void Clear() => _list.Clear();
        public bool Contains(IDisposable item) => _list.Contains(item);
        public void CopyTo(IDisposable[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);
        public bool Remove(IDisposable item) => _list.Remove(item);
        public IEnumerator<IDisposable> GetEnumerator() => _list.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
    }

    public sealed class SerialDisposable : IDisposable
    {
        private IDisposable _current;
        private bool _disposed;

        public IDisposable Disposable
        {
            get => _current;
            set
            {
                if (_disposed)
                {
                    value?.Dispose();
                    return;
                }
                var old = _current;
                _current = value;
                old?.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _current?.Dispose();
            _current = null;
        }
    }

    public static class DisposableExtensions
    {
        public static T AddTo<T>(this T disposable, CompositeDisposable composite) where T : IDisposable
        {
            composite.Add(disposable);
            return disposable;
        }

        public static T AddTo<T>(this T disposable, SerialDisposable serial) where T : IDisposable
        {
            serial.Disposable = disposable;
            return disposable;
        }
    }
}
