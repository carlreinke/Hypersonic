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
using Microsoft.EntityFrameworkCore.Extensions.Internal;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hypersonic
{
    internal static class IQueryableExtensions
    {
        public static Task ForEachAwaitAsync<TSource>(this IQueryable<TSource> source, Func<TSource, Task> action, CancellationToken cancellationToken)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            return source.AsAsyncEnumerable().ForEachAwaitAsync(action, cancellationToken);
        }

        public static Task ForEachAwaitAsync<TSource>(this IQueryable<TSource> source, Func<TSource, CancellationToken, Task> action, CancellationToken cancellationToken)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            return source.AsAsyncEnumerable().ForEachAwaitAsync(action, cancellationToken);
        }
    }
}
