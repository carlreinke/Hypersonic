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
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Hypersonic
{
    internal static class DbContextExtensions
    {
        public static IQueryable<TProperty> QueryCollection<TEntity, TProperty>(this DbContext dbContext, TEntity entity, Expression<Func<TEntity, IEnumerable<TProperty>>> propertyExpression)
            where TEntity : class
            where TProperty : class
        {
            if (dbContext == null)
                throw new ArgumentNullException(nameof(dbContext));

            return dbContext.Entry(entity).Collection(propertyExpression).Query();
        }

        public static void LoadCollection<TEntity, TProperty>(this DbContext dbContext, TEntity entity, Expression<Func<TEntity, IEnumerable<TProperty>>> propertyExpression)
            where TEntity : class
            where TProperty : class
        {
            if (dbContext == null)
                throw new ArgumentNullException(nameof(dbContext));

            dbContext.Entry(entity).Collection(propertyExpression).Load();
        }
    }
}
