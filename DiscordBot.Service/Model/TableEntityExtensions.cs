using Microsoft.Azure.Cosmos.Table;
using Microsoft.Azure.Cosmos.Table.Queryable;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
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

        private static PropertyInfo GetPropertyInfo<TSource, TProperty>(Expression<Func<TSource, TProperty>> propertyLambda)
        {
            Type type = typeof(TSource);

            MemberExpression member = propertyLambda.Body as MemberExpression;
            if (member == null)
                throw new ArgumentException(string.Format(
                    "Expression '{0}' refers to a method, not a property.",
                    propertyLambda.ToString()));

            PropertyInfo propInfo = member.Member as PropertyInfo;
            if (propInfo == null)
                throw new ArgumentException(string.Format(
                    "Expression '{0}' refers to a field, not a property.",
                    propertyLambda.ToString()));

            if (type != propInfo.ReflectedType &&
                !type.IsSubclassOf(propInfo.ReflectedType))
                throw new ArgumentException(string.Format(
                    "Expression '{0}' refers to a property that is not from type {1}.",
                    propertyLambda.ToString(),
                    type));

            return propInfo;
        }


        public static async Task LoadChildrens<TParent, TChild>(this TParent parent, Expression<Func<TParent, ICollection<TChild>>> memberToLoad) where TParent : TableEntity where TChild : TableEntity, new()
        {
            var propInfo = GetPropertyInfo(memberToLoad);

            var childType = propInfo.PropertyType.GetGenericArguments()[0];

            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(Configuration["secret-azure-tables"]);
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            var tableName = childType.Name;
            CloudTable table = tableClient.GetTableReference(tableName);
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

            var childType = propInfo.PropertyType;

            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(Configuration["secret-azure-tables"]);
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            var tableName = childType.Name;
            CloudTable table = tableClient.GetTableReference(tableName);
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
    }
}
