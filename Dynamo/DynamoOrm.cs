using System;
using System.Linq;
using System.Reflection;

namespace DynamoOrm
{
    [AttributeUsage(AttributeTargets.Property)]
    public class IgnoreAttribute : Attribute
    {
    }
    [AttributeUsage(AttributeTargets.Class)]
    public class TableAttribute : Attribute
    {
        public string Name { get; }
        public TableAttribute(string name) => Name = name;
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class IdAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property)]
    public class ColumnAttribute : Attribute
    {
        public string Name { get; }
        public ColumnAttribute(string name) => Name = name;
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class DbTypeAttribute : Attribute
    {
        public string SqlType { get; }
        public DbTypeAttribute(string sqlType) => SqlType = sqlType;
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class OnStoreAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class OnRetrieveAttribute : Attribute { }

    public abstract class DynamoEntity
    {
        /// <summary>
        /// Runs all methods marked with [OnStore]
        /// </summary>
        public void RunOnStore()
        {
            var methods = this.GetType()
                              .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                              .Where(m => m.GetCustomAttribute<OnStoreAttribute>() != null);

            foreach (var method in methods)
                method.Invoke(this, null);
        }

        /// <summary>
        /// Runs all methods marked with [OnRetrieve]
        /// </summary>
        public void RunOnRetrieve()
        {
            var methods = this.GetType()
                              .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                              .Where(m => m.GetCustomAttribute<OnRetrieveAttribute>() != null);

            foreach (var method in methods)
                method.Invoke(this, null);
        }

        /// <summary>
        /// Gets the table name from the [Table] attribute
        /// </summary>
        public string GetTableName()
        {
            var attr = this.GetType().GetCustomAttribute<TableAttribute>();
            return attr?.Name ?? this.GetType().Name;
        }

        /// <summary>
        /// Gets the ID property and value
        /// </summary>
        public PropertyInfo GetIdProperty()
        {
            return this.GetType()
                       .GetProperties()
                       .FirstOrDefault(p => p.GetCustomAttribute<IdAttribute>() != null);
        }

        public object GetIdValue()
        {
            var idProp = GetIdProperty();
            return idProp?.GetValue(this);
        }

        /// <summary>
        /// Assigns new Guid to ID if it's null or empty
        /// </summary>
        public void EnsureId()
        {
            var idProp = GetIdProperty();
            if (idProp != null && idProp.PropertyType == typeof(Guid))
            {
                var current = (Guid)idProp.GetValue(this);
                if (current == Guid.Empty)
                    idProp.SetValue(this, Guid.NewGuid());
            }
        }
    }
}
