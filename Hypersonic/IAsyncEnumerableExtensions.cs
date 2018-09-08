//
// Copyright (C) 2018  Carl Reinke
//
// This file is part of Hypersonic.
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without
// even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// General Public License for more details.
//
// You should have received a copy of the GNU General Public License along with this program.  If
// not, see <https://www.gnu.org/licenses/>.
//
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Hypersonic
{
    internal static class IAsyncEnumerableExtensions
    {
        public static async Task ForEachAwaitAsync<TSource>(this IAsyncEnumerable<TSource> source, Func<TSource, Task> action, CancellationToken cancellationToken)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            using (IAsyncEnumerator<TSource> e = source.GetEnumerator())
            {
                while (await e.MoveNext(cancellationToken).ConfigureAwait(false))
                {
                    await action(e.Current).ConfigureAwait(false);
                }
            }
        }

        public static async Task ForEachAwaitAsync<TSource>(this IAsyncEnumerable<TSource> source, Func<TSource, CancellationToken, Task> action, CancellationToken cancellationToken)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            using (IAsyncEnumerator<TSource> e = source.GetEnumerator())
            {
                while (await e.MoveNext(cancellationToken).ConfigureAwait(false))
                {
                    await action(e.Current, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        public static IAsyncEnumerable<TResult> SelectAwaitAsync<TSource, TResult>(this IAsyncEnumerable<TSource> source, Func<TSource, Task<TResult>> selector)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (selector == null)
                throw new ArgumentNullException(nameof(selector));

            return new SelectAwaitAsyncIterator<TSource, TResult>(source, (sourceItem, cancellationToken) => selector(sourceItem));
        }

        public static IAsyncEnumerable<TResult> SelectAwaitAsync<TSource, TResult>(this IAsyncEnumerable<TSource> source, Func<TSource, CancellationToken, Task<TResult>> selector)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (selector == null)
                throw new ArgumentNullException(nameof(selector));

            return new SelectAwaitAsyncIterator<TSource, TResult>(source, selector);
        }

        private enum AsyncIteratorState
        {
            New = 0,
            Allocated = 1,
            Iterating = 2,
            Disposed = -1
        }

        private abstract class AsyncIterator<TSource> : IAsyncEnumerable<TSource>, IAsyncEnumerator<TSource>, IDisposable
        {
            private protected AsyncIteratorState _state;

            private protected TSource _current;

            public TSource Current
            {
                get
                {
                    if (_state != AsyncIteratorState.Iterating)
                        throw new InvalidOperationException("Enumerator is in an invalid state.");

                    return _current;
                }
            }

            public virtual void Dispose()
            {
                _state = AsyncIteratorState.Disposed;
                _current = default;
            }

            public IAsyncEnumerator<TSource> GetEnumerator()
            {
                AsyncIterator<TSource> asyncIterator = _state == AsyncIteratorState.New ? this : Clone();
                asyncIterator._state = AsyncIteratorState.Allocated;
                return asyncIterator;
            }

            public async Task<bool> MoveNext(CancellationToken cancellationToken)
            {
                if (_state == AsyncIteratorState.Disposed)
                    return false;

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return await MoveNextCore(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public abstract AsyncIterator<TSource> Clone();

            protected abstract Task<bool> MoveNextCore(CancellationToken cancellationToken);
        }

        private sealed class SelectAwaitAsyncIterator<TSource, TResult> : AsyncIterator<TResult>
        {
            private readonly IAsyncEnumerable<TSource> _source;

            private readonly Func<TSource, CancellationToken, Task<TResult>> _selector;

            private IAsyncEnumerator<TSource> _enumerator;

            public SelectAwaitAsyncIterator(IAsyncEnumerable<TSource> source, Func<TSource, CancellationToken, Task<TResult>> selector)
            {
                _source = source;
                _selector = selector;
            }

            public override void Dispose()
            {
                if (_enumerator != null)
                    _enumerator.Dispose();

                base.Dispose();
            }

            public override AsyncIterator<TResult> Clone()
            {
                return new SelectAwaitAsyncIterator<TSource, TResult>(_source, _selector);
            }

            protected override async Task<bool> MoveNextCore(CancellationToken cancellationToken)
            {
                switch (_state)
                {
                    case AsyncIteratorState.Allocated:
                        _enumerator = _source.GetEnumerator();
                        _state = AsyncIteratorState.Iterating;
                        goto case AsyncIteratorState.Iterating;
                    case AsyncIteratorState.Iterating:
                        if (await _enumerator.MoveNext(cancellationToken).ConfigureAwait(false))
                        {
                            _current = await _selector(_enumerator.Current, cancellationToken).ConfigureAwait(false);
                            return true;
                        }
                        else
                        {
                            Dispose();
                            return false;
                        }
                    default:
                        return false;
                }
            }
        }
    }
}
