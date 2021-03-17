using Microsoft.ApplicationInsights;
using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.Cosmos.Table.Queryable;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace DiscordBot.Service.Model
{
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    sealed class ParentAttribute : Attribute
    {
        // This is a positional argument
        public ParentAttribute(Type parentType)
        {
            this.ParentType = parentType;
        }

        public Type ParentType { get; set; }
    }

    public static class TableEntityExtensions
    {
        public static IConfiguration Configuration;

        public static TelemetryClient TelemetryClient;

        private static PropertyInfo GetPropertyInfo<TSource, TProperty>(Expression<Func<TSource, TProperty>> propertyLambda)
        {
            Type type = typeof(TSource);

            MemberExpression member = propertyLambda.Body as MemberExpression;
            if (member == null)
                throw new ArgumentException(
                    $"Expression '{propertyLambda}' refers to a method, not a property.");

            PropertyInfo propInfo = member.Member as PropertyInfo;
            if (propInfo == null)
                throw new ArgumentException(
                    $"Expression '{propertyLambda}' refers to a field, not a property.");

            if (type != propInfo.ReflectedType &&
                !type.IsSubclassOf(propInfo.ReflectedType))
                throw new ArgumentException(
                    $"Expression '{propertyLambda}' refers to a property that is not from type {type}.");

            return propInfo;
        }

        public static CloudTable GetTable<T>() where T : TableEntity
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(Configuration["secret-azure-tables"]);
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            var tableName = typeof(T).Name;
            CloudTable table = tableClient.GetTableReference(tableName);

            return table;
        }

        public static async Task<CloudTable> GetTableAndCreate<T>() where T : TableEntity
        {
            var table = GetTable<T>();

            var created = await table.CreateIfNotExistsAsync();

            TelemetryClient.TrackEvent($"Accessing {table.Name}, table was {(created ? string.Empty : "NOT")} created");

            return table;
        }

        public static async Task LoadChildrens<TParent, TChild>(this TParent parent, Expression<Func<TParent, ICollection<TChild>>> memberToLoad) where TParent : TableEntity where TChild : TableEntity, new()
        {
            var propInfo = GetPropertyInfo(memberToLoad);

            TelemetryClient.TrackEvent($"Loading {typeof(TParent).Name} children to : {propInfo.Name}");

            var childType = propInfo.PropertyType.GetGenericArguments()[0];

            var table = GetTable<TChild>();
            var creation = await table.CreateIfNotExistsAsync();

            //If the table was just created, we populate the field with an empty collection and return.
            if (creation)
            {
                var childrens = new Collection<TChild>();
                propInfo.SetValue(parent, childrens);

                return;
            }

            var query = table.CreateQuery<TChild>().Where(child => child.PartitionKey == parent.RowKey).AsTableQuery();

            List<TChild> result = new List<TChild>();

            TableContinuationToken token = null;
            do
            {
                var partialResult = await table.ExecuteQuerySegmentedAsync(query, token);
                token = partialResult.ContinuationToken;
                result.AddRange(partialResult);
            }
            while (token != null);


            var parentProp = childType.GetProperties(BindingFlags.Public | BindingFlags.Instance).SingleOrDefault(prop => prop.GetCustomAttributes<ParentAttribute>(true).Any(att => att.ParentType == typeof(TParent)));

            //If a property referencing a parent of the type we got was marked, we link them up.
            if (parentProp != null)
            {
                foreach (var value in result)
                {
                    parentProp.SetValue(value, parent);
                }
            }

            propInfo.SetValue(parent, result);
        }

        public static async Task LoadChild<TParent, TChild>(this TParent parent, Expression<Func<TParent, TChild>> child) where TParent : TableEntity where TChild : TableEntity, new()
        {
            var propInfo = GetPropertyInfo(child);

            TelemetryClient.TrackEvent($"Loading {typeof(TParent).Name} child property to : {propInfo.Name}");

            var childType = propInfo.PropertyType;

            var table = GetTable<TChild>();

            var creation = await table.CreateIfNotExistsAsync();

            //If the table was just created, we populate the field with an empty collection and return.
            if (creation)
            {
                var childrens = new Collection<TChild>();
                propInfo.SetValue(parent, childrens);

                return;
            }

            var query = table.CreateQuery<TChild>().Where(c => c.PartitionKey == parent.RowKey).AsTableQuery();

            var result = table.ExecuteQuery(query).Single();

            var parentProp = childType.GetProperties(BindingFlags.Public | BindingFlags.Instance).SingleOrDefault(prop => prop.GetCustomAttributes<ParentAttribute>(true).Any(att => att.ParentType == typeof(TParent)));

            //If a property referencing a parent of the type we got was marked, we link them up.
            if (parentProp != null)
            {
                parentProp.SetValue(result, parent);
            }

            propInfo.SetValue(parent, result);
        }

        public static async Task<IQueryable<TEntity>> SearchAndCreateTableAsync<TEntity>(Expression<Func<TEntity, bool>> searchExpression) where TEntity : TableEntity, new()
        {
            var table = await GetTableAndCreate<TEntity>();

            return table.CreateQuery<TEntity>().Where(searchExpression);
        }

        public static IQueryable<TEntity> SearchTable<TEntity>(Expression<Func<TEntity, bool>> searchExpression) where TEntity : TableEntity, new()
        {
            var table = GetTable<TEntity>();

            return table.CreateQuery<TEntity>().Where(searchExpression);
        }

        public static TEntity GetOne<TEntity>(this IQueryable<TEntity> query) where TEntity : TableEntity, new()
        {
            return query.Take(1).AsTableQuery().Execute().FirstOrDefault();
        }

        public static async Task<IList<TEntity>> GetCollectionAsync<TEntity>(this IQueryable<TEntity> query) where TEntity : TableEntity, new()
        {
            return await query.GetCollectionAsyncInternal().ToListAsync();
        }

        private static async IAsyncEnumerable<TEntity> GetCollectionAsyncInternal<TEntity>(this IQueryable<TEntity> query) where TEntity : TableEntity, new()
        {
            var tableQuery = query.AsTableQuery();

            TableContinuationToken token = null;

            do
            {
                var partialResult = await tableQuery.ExecuteSegmentedAsync(token);

                token = partialResult.ContinuationToken;

                foreach (var result in partialResult.Results)
                {
                    yield return result;
                }

            } while (token != null);
        }

        public static async Task InsertAsync<TEntity>(this TEntity entity) where TEntity : TableEntity, new()
        {
            var table = await GetTableAndCreate<TEntity>();

            var tableOp = TableOperation.Insert(entity);

            await table.ExecuteAsync(tableOp);
        }

        public static async Task DeleteAsync<TEntity>(this TEntity entity) where TEntity : TableEntity, new()
        {
            var table = GetTable<TEntity>();

            var tableOp = TableOperation.Delete(entity);

            await table.ExecuteAsync(tableOp);
        }

        public static async Task DeleteBatchAsync<TEntity>(Expression<Func<TEntity, bool>> filter) where TEntity : TableEntity, new()
        {
            var table = await GetTableAndCreate<TEntity>();

            var filterQuery = table.CreateQuery<TEntity>().Where(filter).AsTableQuery();

            var batchDelete = new TableBatchOperation();

            TableContinuationToken token = null;
            do
            {
                var partialResult = await table.ExecuteQuerySegmentedAsync(filterQuery, token);
                
                token = partialResult.ContinuationToken;

                foreach (var result in partialResult)
                {
                    batchDelete.Delete(result);
                }

            } while (token != null);

            await table.ExecuteBatchAsync(batchDelete);
        }
    }
}
