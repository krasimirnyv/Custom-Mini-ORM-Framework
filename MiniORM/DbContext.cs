namespace MiniORM
{
    using Microsoft.Data.SqlClient;
    
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Reflection;

    using static ExceptionMessages;
    
    /// <summary>
    /// Represents the central unit of work for interacting with the database. 
    /// A <c>DbContext</c> provides the following responsibilities:
    /// 
    /// <para><b>Connection management</b></para>
    /// - Establishes and manages the underlying database connection.
    /// - Ensures that all operations are executed within an open connection and transaction.
    /// 
    /// <para><b>Entity sets</b></para>
    /// - Discovers and initializes <c>DbSet</c> properties that map to database tables.
    /// - Loads entities from the database into local collections for querying and manipulation.
    /// 
    /// <para><b>Change tracking</b></para>
    /// - Keeps track of entity state through the <c>ChangeTracker</c>.
    /// - Identifies added, modified, and removed entities during the lifetime of the context.
    /// 
    /// <para><b>Relationships</b></para>
    /// - Maps foreign keys and navigation properties between entities.
    /// - Supports one-to-many and many-to-many relationships via navigation collections.
    /// 
    /// <para><b>Persistence</b></para>
    /// - Validates entities against data annotations before saving.
    /// - Persists changes (inserts, updates, deletes) back to the database.
    /// - Executes operations inside a transaction, rolling back on exceptions 
    ///   and committing only if successful.
    /// 
    /// In summary, the <c>DbContext</c> coordinates the mapping between the object model 
    /// and the underlying relational database, serving as the main entry point for data access.
    /// </summary>
    public abstract class DbContext
    {
        private readonly DatabaseConnection _connection;
        private readonly IDictionary<Type, PropertyInfo> _dbSetProperties;

        protected DbContext(string connectionString)
        {
            this._connection = new DatabaseConnection(connectionString);
            this._dbSetProperties = this.DiscoverDbSets();

            using (new ConnectionManager(this._connection))
            {
                this.InitializeDbSets();
            }
            
            this.MapAllRelations(); // This is done after connection close because it is in-memory
        }
        
        internal static readonly ICollection<Type> AllowedSqlTypes = new HashSet<Type>()
        {
            // TODO: Add nullable types as well
            
            // String data types
            typeof(string),
            typeof(char), // Represents SQL CHAR(1) / NCHAR(1)

            // Numeric data types
            typeof(bool),
            typeof(byte),
            typeof(short),
            typeof(int),
            typeof(long),
            typeof(float),
            typeof(double),
            typeof(decimal),

            // Date and Time data types
            typeof(DateTime),
            typeof(DateOnly),
            typeof(TimeOnly),
            typeof(TimeSpan),
            typeof(DateTimeOffset),

            // Binary data types
            typeof(byte[]),
            
            // Unique Identifier
            typeof(Guid)
        };

        /// <summary>
        /// Persists all changes made in the tracked <c>DbSet</c> instances to the database.
        /// 
        /// <para><b>Validation</b></para>
        /// - Ensures all entities are valid according to their data annotations.
        /// - Throws an <see cref="InvalidOperationException"/> if invalid entities are detected.
        /// 
        /// <para><b>Persistence</b></para>
        /// - Inserts newly added entities, updates modified entities, 
        ///   and deletes removed entities using the <c>ChangeTracker</c>.
        /// 
        /// <para><b>Transaction management</b></para>
        /// - Executes all operations within a single database transaction.
        /// - Rolls back the transaction if an exception occurs during persistence.
        /// - Commits the transaction if all operations succeed.
        /// </summary>
        public void SaveChanges()
        {
            IEnumerable<object> dbSetObjects = _dbSetProperties
                .Select(pi => pi.Value.GetValue(this))
                .ToArray()!;

            foreach (IEnumerable<object> dbSet in dbSetObjects)
            {
                IEnumerable<object> invalidEntities = dbSet
                    .Where(entity => !IsObjectValid(entity))
                    .ToList();

                if (invalidEntities.Any())
                {
                    throw new InvalidOperationException(string.Format(InvalidEntitiesInDbSetMessage,
                        invalidEntities.Count(), dbSet.GetType().Name));
                }
            }
            
            using (new ConnectionManager(this._connection))
            {
                using (SqlTransaction transaction = this._connection.StartTransaction())
                {
                    foreach (IEnumerable<object> dbSet in dbSetObjects)
                    {
                        MethodInfo? persistMethodGeneric = this.GetType()
                            .GetMethod(nameof(Persist), BindingFlags.Instance | BindingFlags.NonPublic)?
                            .MakeGenericMethod(dbSet.GetType());

                        try
                        {
                            try
                            {
                                persistMethodGeneric?.Invoke(this, new object[] { dbSet });
                            }
                            catch (TargetInvocationException tie)
                                when(tie.InnerException is not null)
                            {
                                throw tie.InnerException;
                            }
                        }
                        catch
                        {
                            Console.WriteLine(TransactionRollbackMessage);
                            transaction.Rollback();
                            throw;
                        }
                    }

                    try
                    {
                        transaction.Commit();
                    }
                    catch
                    {
                        Console.WriteLine(TransactionExceptionMessage);
                        throw;
                    }
                }
            }
        }

        private static bool IsObjectValid(object obj)
        {
            ValidationContext validationContext = new ValidationContext(obj);
            ICollection<ValidationResult> validationErrors = new List<ValidationResult>();
            
            return Validator.TryValidateObject(obj, validationContext, validationErrors, true);
        }
        
        private void Persist<TEntity>(DbSet<TEntity> dbSet)
            where TEntity : class, new()
        {
            string tableName = this.GetTableName(typeof(TEntity));
            string[] columnNames = this._connection
                .FetchColumnNames(tableName)
                .ToArray();

            if (dbSet.ChangeTracker.Added.Count != 0)
            {
                this._connection.InsertEntities(dbSet.ChangeTracker.Added, tableName, columnNames);
            }

            IEnumerable<TEntity> modifiedEntities = dbSet.ChangeTracker
                .GetModifiedEntities(dbSet);

            if (modifiedEntities.Any())
            {
                this._connection.UpdateEntities(modifiedEntities, tableName, columnNames);
            }

            if (dbSet.ChangeTracker.Removed.Count != 0)
            {
                this._connection.DeleteEntities(dbSet.ChangeTracker.Removed, tableName, columnNames);
            }
        }
        
        private IDictionary<Type, PropertyInfo> DiscoverDbSets()
        { 
            return this.GetType()
                .GetProperties()
                .Where(pi => pi.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
                .ToDictionary(pi => pi.PropertyType.GetGenericArguments().First(), pi => pi);
        }
        
        private void InitializeDbSets()
        {
            foreach (KeyValuePair<Type, PropertyInfo> dbSetPropertyKvp in _dbSetProperties)
            {
                Type entityType = dbSetPropertyKvp.Key;
                PropertyInfo dbSetProperty = dbSetPropertyKvp.Value;

                MethodInfo? populateDbSetMethodGeneric = this.GetType()
                    .GetMethod(nameof(PopulateDbSet), BindingFlags.Instance | BindingFlags.NonPublic)?
                    .MakeGenericMethod(entityType);

                if (populateDbSetMethodGeneric is null)
                {
                    throw new InvalidOperationException(PopulateDbSetNotFoundMessage);
                }
                
                populateDbSetMethodGeneric.Invoke(this, new object[] { dbSetProperty });
            }
        }

        private void PopulateDbSet<TEntity>(PropertyInfo dbSetPropertyInfo)
            where TEntity : class, new()
        {
            IEnumerable<TEntity> dbSetEntities = this.LoadTableEntities<TEntity>();
            DbSet<TEntity> dbSetInstance = new DbSet<TEntity>(dbSetEntities);
            
            ReflectionHelper.ReplaceBackingField(this, dbSetPropertyInfo.Name, dbSetInstance);
        }
        
        private IEnumerable<TEntity> LoadTableEntities<TEntity>()
            where TEntity : class, new()
        {
            Type entityType = typeof(TEntity);
            string tableName = this.GetTableName(entityType);
            string[] columnNames = this.GetEntityColumnNames(entityType).ToArray();
            
            return this._connection.FetchResultSet<TEntity>(tableName, columnNames);
        }
        
        private IEnumerable<string> GetEntityColumnNames(Type entityType)
        {
            string tableName = this.GetTableName(entityType);
            IEnumerable<string> columnNames = this._connection
                .FetchColumnNames(tableName);

            IEnumerable<string> entityColumnNames = entityType
                .GetProperties()
                .Where(pi => columnNames.Contains(pi.Name, StringComparer.InvariantCultureIgnoreCase) &&
                             !pi.HasAttribute<NotMappedAttribute>() &&
                             AllowedSqlTypes.Contains(pi.PropertyType))
                .Select(pi => pi.Name);
            
            return entityColumnNames;
        }
        
        private string GetTableName(Type tableType)
        {
            Attribute? tableNameAttribute = Attribute.GetCustomAttribute(tableType, typeof(TableAttribute));

            if (tableNameAttribute is null)
            {
                return this._dbSetProperties[tableType].Name;
            }

            if (tableNameAttribute is TableAttribute tableNameAttributeConfig)
            {
                return tableNameAttributeConfig.Name;
            }

            throw new ArgumentException(string.Format(NoTableNameFound, this._dbSetProperties[tableType].Name));
        }
        
        private void MapAllRelations()
        {
            foreach (KeyValuePair<Type, PropertyInfo> dbSetPropertyKvp in _dbSetProperties)
            {
                Type entityType = dbSetPropertyKvp.Key;
                PropertyInfo dbSetProperty = dbSetPropertyKvp.Value;
                
                object? dbSetInstance = dbSetProperty.GetValue(this);
                if (dbSetInstance is null)
                {
                    throw new InvalidOperationException(string.Format(NullDbSetMessage, dbSetProperty.Name));
                }
                
                MethodInfo? mapRelationsMethodGeneric = this.GetType()
                    .GetMethod(nameof(MapRelations), BindingFlags.Instance | BindingFlags.NonPublic)?
                    .MakeGenericMethod(entityType);

                if (mapRelationsMethodGeneric is null)
                {
                    throw new InvalidOperationException(MapRelationsNotFoundMessage);
                }

                mapRelationsMethodGeneric.Invoke(this, new object[] { dbSetInstance });
            }
        }
        
        private void MapRelations<TEntity>(DbSet<TEntity> dbSetInstance)
            where TEntity : class, new()
        {
            Type entityType = typeof(TEntity);
            this.MapNavigationProperties(dbSetInstance);

            IEnumerable<PropertyInfo> navigationCollections = entityType
                .GetProperties()
                .Where(pi => pi.PropertyType.IsGenericType &&
                             pi.PropertyType.GetGenericTypeDefinition() == typeof(ICollection<>) &&
                             this._dbSetProperties.ContainsKey(pi.PropertyType.GetGenericArguments().First()));

            foreach (PropertyInfo navigationCollectionPropertyInfo in navigationCollections)
            {
                Type collectionEntityType = navigationCollectionPropertyInfo
                    .PropertyType
                    .GenericTypeArguments
                    .First();
                
                MethodInfo? mapCollectionMethodGeneric = this.GetType()
                    .GetMethod(nameof(MapNavigationCollection), BindingFlags.Instance | BindingFlags.NonPublic)?
                    .MakeGenericMethod(entityType, collectionEntityType);

                if (mapCollectionMethodGeneric is null)
                {
                    throw new InvalidOperationException(MapNavigationCollectionNotFoundMessage);
                }
                
                mapCollectionMethodGeneric.Invoke(this, new object[] { dbSetInstance, navigationCollectionPropertyInfo });
            }
        }

        private void MapNavigationProperties<TEntity>(DbSet<TEntity> dbSetInstance)
            where TEntity : class, new()
        {
            Type entityType = typeof(TEntity);

            IEnumerable<PropertyInfo> foreignKeyProperties = entityType
                .GetProperties()
                .Where(pi => pi.HasAttribute<ForeignKeyAttribute>());

            foreach (PropertyInfo foreignKeyPropertyInfo in foreignKeyProperties)
            {
                string navigationPropertyName = foreignKeyPropertyInfo
                    .GetCustomAttribute<ForeignKeyAttribute>()!.Name;

                PropertyInfo? navigationPropertyInfo = entityType
                    .GetProperties()
                    .FirstOrDefault(pi => pi.Name == navigationPropertyName);

                if (navigationPropertyInfo is null)
                {
                    throw new ArgumentException(string.Format(InvalidNavigationPropertyName,
                        foreignKeyPropertyInfo.Name, navigationPropertyName));
                }

                object? navigationDbSetInstance = this._dbSetProperties[navigationPropertyInfo.PropertyType]
                    .GetValue(this);

                if (navigationDbSetInstance is null)
                {
                    throw new InvalidOperationException(string.Format(NavPropertyWithoutDbSetMessage,
                        navigationPropertyInfo.Name, navigationPropertyInfo.PropertyType));
                }
                
                PropertyInfo navigationPrimaryKey = navigationPropertyInfo
                    .PropertyType
                    .GetProperties()
                    .First(pi => pi.HasAttribute<KeyAttribute>());

                foreach (TEntity entity in dbSetInstance)
                {
                    object? foreignKeyValue = foreignKeyPropertyInfo.GetValue(entity);

                    if (foreignKeyValue is null)
                    {
                        navigationPropertyInfo.SetValue(entity, null);
                        continue;
                    }

                    object navigationEntity = ((IEnumerable<object>)navigationDbSetInstance)
                        .First(currNavPropEntity => navigationPrimaryKey
                            .GetValue(currNavPropEntity)
                            !.Equals(foreignKeyValue));
                    
                    navigationPropertyInfo.SetValue(entity, navigationEntity);
                }
            }
        }

        // TODO: Investigate mapping of many-to-many relations
        private void MapNavigationCollection<TEntity, TCollectionEntity>(DbSet<TEntity> dbSetInstance, PropertyInfo navigationCollectionPropertyInfo)
        where TEntity : class, new()
        where TCollectionEntity : class, new()
        {
            Type entityType = typeof(TEntity);
            Type collectionEntityType = typeof(TCollectionEntity);

            IEnumerable<PropertyInfo> collectionEntityPrimaryKeys = collectionEntityType
                .GetProperties()
                .Where(pi => pi.HasAttribute<KeyAttribute>());
            
            PropertyInfo foreignKeyProperties = collectionEntityType
                .GetProperties()
                .First(pi => pi.HasAttribute<ForeignKeyAttribute>() && 
                                                collectionEntityType
                                                    .GetProperty(pi.GetCustomAttribute<ForeignKeyAttribute>()!.Name)!
                                                    .PropertyType == entityType);
            
            PropertyInfo entityPrimaryKey = entityType
                .GetProperties()
                .First(pi => pi.HasAttribute<KeyAttribute>());
            
            
            /* TODO: not quite sure about this part:
             
            PropertyInfo collectionPrimaryKey = collectionEntityPrimaryKeys.First();

            bool isManyToMany = collectionEntityPrimaryKeys.Length > 1;
            if (isManyToMany)
            {
                collectionPrimaryKey = collectionEntityType
                    .GetProperties()
                    .First(pi => collectionEntityType
                        .GetProperty(pi.GetCustomAttribute<ForeignKeyAttribute>().Name)
                        .PropertyType == entityType);
            }
            */

            DbSet<TCollectionEntity>? navigationCollectionDbSet = (DbSet<TCollectionEntity>?)this._dbSetProperties[collectionEntityType].GetValue(this)!;

            if (navigationCollectionDbSet is null)
            {
                throw new InvalidOperationException(string.Format(NullCollectionMessage, navigationCollectionDbSet, entityType.Name));
            }
            
            foreach (TEntity entity in dbSetInstance)
            {
                object entityPrimaryKeyValue = entityPrimaryKey.GetValue(entity)!;
                ICollection<TCollectionEntity> navigationEntities = navigationCollectionDbSet
                    .Where(navEntity => foreignKeyProperties.GetValue(navEntity) != null &&
                                        foreignKeyProperties.GetValue(navEntity)!.Equals(entityPrimaryKeyValue))
                    .ToArray();
                
                ReflectionHelper.ReplaceBackingField(dbSetInstance, navigationCollectionPropertyInfo.Name, navigationEntities);
            }
        }
    }
}
