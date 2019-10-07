﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Csg.Data.ListQuery.Abstractions;
using Csg.Data.Sql;

namespace Csg.Data.ListQuery
{
    public static class ListQueryExtensions
    {
        public static IListQueryBuilder ValidateWith<TValidation>(this IListQueryBuilder listQuery) where TValidation : class, new()
        {
            return ValidateWith(listQuery, typeof(TValidation));
        }

        public static IListQueryBuilder ValidateWith(this IListQueryBuilder listQuery, Type validationType)
        {
            listQuery.Configuration.ShouldValidate = true;

            var properties = Internal.ReflectionHelper.GetListPropertyInfo(validationType);

            foreach (var property in properties)
            {
                listQuery.Configuration.Validations.Add(property.Key, property.Value);
            }

            return listQuery;
        }

        public static IListQueryBuilder ValidateWith(this IListQueryBuilder listQuery, IEnumerable<ListPropertyInfo> fields)
        {
            listQuery.Configuration.ShouldValidate = true;

            foreach (var field in fields)
            {
                listQuery.Configuration.Validations.Add(field.Name, field);
            }

            return listQuery;
        }

        public static IListQueryBuilder AddFilterHandler(this IListQueryBuilder listQuery, string name, ListQueryFilterHandler handler)
        {
            listQuery.Configuration.Handlers.Add(name, handler);

            return listQuery;
        }

        public static IListQueryBuilder AddFilterHandlers<THandlers>(this IListQueryBuilder listQuery) where THandlers : class, new()
        {
            return AddFilterHandlers(listQuery, typeof(THandlers));
        }

        public static IListQueryBuilder AddFilterHandlers(this IListQueryBuilder listQuery, Type handlersType)
        {
            var methods = handlersType.GetMethods(BindingFlags.Static | BindingFlags.Public);

            foreach (var method in methods)
            {
                var handler = (ListQueryFilterHandler)ListQueryFilterHandler.CreateDelegate(typeof(ListQueryFilterHandler), method);
                //TODO: Cache these
                //ListQueryFilterHandler handler = (where, filter, config) =>
                //{
                //    method.Invoke(null, new object[] { where, filter, config });
                //};

                listQuery.Configuration.Handlers.Add(method.Name, handler);
            }

            return listQuery;
        }
        
        public static IListQueryBuilder RemoveHandler(this IListQueryBuilder listQuery, string name)
        {
            listQuery.Configuration.Handlers.Remove(name);
            return listQuery;
        }

        public static IListQueryBuilder NoValidation(this IListQueryBuilder listQuery)
        {
            listQuery.Configuration.ShouldValidate = false;
            return listQuery;
        }

        //public static IDbQueryBuilder CreateCacheKey(this IDbQueryBuilder builder, out string cacheKey, string prefix = "QueryBuilder:")
        //{
        //    cacheKey = builder.CreateCacheKey(prefix: prefix);
        //    return builder;
        //}

        //public static string CreateCacheKey(this IDbQueryBuilder builder, string prefix = "QueryBuilder:")
        //{
        //    var hash = System.Security.Cryptography.SHA1.Create();
        //    var stmt = builder.Render();
        //    var buffer = System.Text.UTF8Encoding.UTF8.GetBytes(stmt.CommandText);

        //    hash.TransformBlock(buffer, 0, buffer.Length, null, 0);

        //    foreach (var par in stmt.Parameters)
        //    {
        //        buffer = System.Text.UTF8Encoding.UTF8.GetBytes(par.ParameterName);
        //        hash.TransformBlock(buffer, 0, buffer.Length, null, 0);

        //        buffer = System.Text.UTF8Encoding.UTF8.GetBytes(par.Value.ToString());
        //        hash.TransformBlock(buffer, 0, buffer.Length, null, 0);
        //    }

        //    hash.TransformFinalBlock(buffer, 0, 0);

        //    return string.Concat(prefix, Convert.ToBase64String(hash.Hash));
        //}

        public static void ApplyFilters(IListQueryBuilder listQuery, IDbQueryBuilder queryBuilder)
        {
            if (listQuery.Configuration.QueryDefinition.Filters != null)
            {
                var where = new DbQueryWhereClause(queryBuilder.Root, Sql.SqlLogic.And);

                foreach (var filter in listQuery.Configuration.QueryDefinition.Filters)
                {
                    var hasConfig = listQuery.Configuration.Validations.TryGetValue(filter.Name, out ListPropertyInfo validationField);

                    if (listQuery.Configuration.Handlers.TryGetValue(filter.Name, out ListQueryFilterHandler handler))
                    {
                        handler(where, filter, validationField);
                    }
                    else if (hasConfig || !listQuery.Configuration.ShouldValidate)
                    {
                        where.AddFilter(filter.Name, filter.Operator ?? GenericOperator.Equal, filter.Value, validationField?.DataType ?? System.Data.DbType.String, validationField?.DataTypeSize);
                    }
                    else if (listQuery.Configuration.ShouldValidate)
                    {
                        throw new Exception($"No handler is defined for the filter '{filter.Name}'.");
                    }
                }

                if (where.Filters.Count > 0)
                {
                    queryBuilder.AddFilter(where.Filters);
                }
            }
        }

        public static void ApplySelections(IListQueryBuilder listQuery, IDbQueryBuilder queryBuilder)
        {
            if (listQuery.Configuration.QueryDefinition.Selections != null)
            {
                foreach (var column in listQuery.Configuration.QueryDefinition.Selections)
                {
                    if (listQuery.Configuration.Validations.TryGetValue(column, out ListPropertyInfo config))
                    {
                        queryBuilder.SelectColumns.Add(new Sql.SqlColumn(queryBuilder.Root, config.Name));
                    }
                    else if (listQuery.Configuration.ShouldValidate)
                    {
                        throw new Exception($"The selection field '{column}' does not exist.");
                    }
                    else
                    {
                        queryBuilder.SelectColumns.Add(new Sql.SqlColumn(queryBuilder.Root, column));
                    }
                }
            }
        }

        public static void ApplySort(IListQueryBuilder listQuery, IDbQueryBuilder queryBuilder)
        {
            if (listQuery.Configuration.QueryDefinition.Sort != null)
            {
                foreach (var column in listQuery.Configuration.QueryDefinition.Sort)
                {
                    if (listQuery.Configuration.Validations.TryGetValue(column.Name, out ListPropertyInfo config) && config.IsSortable == true)
                    {
                        queryBuilder.OrderBy.Add(new Sql.SqlOrderColumn()
                        {
                            ColumnName = config.Name,
                            SortDirection = column.SortDescending ? Sql.DbSortDirection.Descending : Sql.DbSortDirection.Ascending
                        });
                    }
                    else if (listQuery.Configuration.ShouldValidate)
                    {
                        throw new Exception($"The sort field '{column.Name}' does not exist.");
                    }
                    else
                    {
                        queryBuilder.OrderBy.Add(new Sql.SqlOrderColumn()
                        {
                            ColumnName = column.Name,
                            SortDirection = column.SortDescending ? Sql.DbSortDirection.Descending : Sql.DbSortDirection.Ascending
                        });
                    }
                }
            }
        }

        public static void ApplyLimit(IListQueryBuilder listQuery, IDbQueryBuilder queryBuilder, bool getTotal = false)
        {
            if (listQuery.Configuration.QueryDefinition.Offset > 0)
            {
                queryBuilder.PagingOptions = new Csg.Data.Sql.SqlPagingOptions()
                {
                    Limit = listQuery.Configuration.QueryDefinition.Limit,
                    Offset = listQuery.Configuration.QueryDefinition.Offset
                };
            }                
        }

        /// <summary>
        /// Applies the given list query configuration and returns a <see cref="IDbQueryBuilder"/>.
        /// </summary>
        /// <param name="listQuery"></param>
        /// <returns></returns>
        public static IDbQueryBuilder Apply(this IListQueryBuilder listQuery)
        {
            var query = listQuery.Configuration.QueryBuilder.Fork();

            ApplySelections(listQuery, query);
            ApplyFilters(listQuery, query);
            ApplySort(listQuery, query);
            ApplyLimit(listQuery, query);
            
            return query;
        }

        public static IDbQueryBuilder GetCountQuery(IListQueryBuilder query)
        {
            var countQuery = query.Apply();

            countQuery.PagingOptions = null;
            countQuery.SelectColumns.Clear();
            countQuery.SelectColumns.Add(new Sql.SqlRawColumn("COUNT(1)"));
            countQuery.OrderBy.Clear();

            return countQuery;           
        }

        public static SqlStatementBatch Render(this Csg.Data.ListQuery.IListQueryBuilder builder, bool queryTotalWhenLimiting = true)
        {
            if (builder.Configuration.QueryDefinition.Limit > 0 && queryTotalWhenLimiting)
            {
                var countQuery = GetCountQuery(builder);
                return new DbQueryBuilder[] { (DbQueryBuilder)countQuery, (DbQueryBuilder)builder.Apply() }
                    .RenderBatch();
            }
            else
            {
                var stmt = builder.Apply().Render();
                return new SqlStatementBatch(1, stmt.CommandText, stmt.Parameters);
            }
        }

        public async static System.Threading.Tasks.Task<ListQueryResult<T>> GetResultAsync<T>(this Csg.Data.ListQuery.IListQueryBuilder builder, bool queryTotalWhenLimiting = true)
        {
            var stmt = builder.Render(queryTotalWhenLimiting);
            var cmd = stmt.ToDapperCommand(builder.Configuration.QueryBuilder.Transaction, builder.Configuration.QueryBuilder.CommandTimeout);

            if (stmt.Count == 1)
            {
                return new ListQueryResult<T>(await Dapper.SqlMapper.QueryAsync<T>(builder.Configuration.QueryBuilder.Connection, cmd));
            }
            else if (stmt.Count == 2)
            {
                using (var batchReader = await Dapper.SqlMapper.QueryMultipleAsync(builder.Configuration.QueryBuilder.Connection, cmd))
                {

                    return new ListQueryResult<T>()
                    {
                        TotalCount = await batchReader.ReadFirstAsync<int>(),
                        Data = await batchReader.ReadAsync<T>()
                    };
                }
            }
            else
            {
                throw new NotSupportedException("A statement with more than 2 queries is not supported.");
            }
        }
    }

}
