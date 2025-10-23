namespace MiniORM
{
    using Microsoft.Data.SqlClient;
    
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Reflection;
    using System.Collections;
    using System.Linq;

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
            // String data types
            typeof(string),
            typeof(char), // Represents SQL CHAR(1) / NCHAR(1)
            typeof(char?),
                
            // Numeric data types
            typeof(bool),
            typeof(bool?),
            typeof(byte),
            typeof(byte?),
            typeof(short),
            typeof(short?),
            typeof(int),
            typeof(int?),
            typeof(long),
            typeof(long?),
            typeof(float),
            typeof(float?),
            typeof(double),
            typeof(double?),
            typeof(decimal),
            typeof(decimal?),

            // Date and Time data types
            typeof(DateTime),
            typeof(DateTime?),
            typeof(DateOnly),
            typeof(DateOnly?),
            typeof(TimeOnly),
            typeof(TimeOnly?),
            typeof(TimeSpan),
            typeof(TimeSpan?),
            typeof(DateTimeOffset),
            typeof(DateTimeOffset?),

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
            IEnumerable<object> dbSetObjects = this._dbSetProperties
                .Select(pi => pi.Value.GetValue(this)!)
                .ToArray();

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
                using SqlTransaction transaction = this._connection.StartTransaction();

                foreach (IEnumerable dbSet in dbSetObjects)
                {
                    MethodInfo? persistMethodGeneric = typeof(DbContext)
                        .GetMethod(nameof(Persist), BindingFlags.Instance | BindingFlags.NonPublic)?
                        .MakeGenericMethod(dbSet.GetType()
                            .GetGenericArguments()
                            .First());

                    /*
                     Logs a summary of entity state changes before persisting the current DbSet.
                     Retrieves Added, Modified, and Removed counts from the ChangeTracker
                     and prints them to the console for debugging and verification.
                    */
                    Type entityType = dbSet
                        .GetType()
                        .GetGenericArguments()
                        .First();
                    
                    string entityTypeName = entityType.Name;
                    
                    // Retrieve the ChangeTracker instance for this DbSet
                    PropertyInfo? changeTrackerProperty = dbSet
                        .GetType()
                        .GetProperty("ChangeTracker");
                    
                    object? changeTracker = changeTrackerProperty?
                        .GetValue(dbSet);

                    // Count Added and Removed entities
                    int addedCount = ((IEnumerable)changeTracker!
                            .GetType().GetProperty("Added")!
                            .GetValue(changeTracker)!)
                        .Cast<object>().Count();

                    int removedCount = ((IEnumerable)changeTracker!
                            .GetType()
                            .GetProperty("Removed")!
                            .GetValue(changeTracker)!)
                        .Cast<object>().Count();

                    // Modified entities are retrieved via GetModifiedEntities(dbSet)
                    MethodInfo? getModifiedMethod = changeTracker
                        .GetType()
                        .GetMethod("GetModifiedEntities");
                    
                    int modifiedCount = ((IEnumerable)getModifiedMethod!
                            .Invoke(changeTracker, new object[] { dbSet })!)
                        .Cast<object>().Count();

                    // Print persistence summary for this entity type
                    Console.WriteLine($"Persisting entity type: {entityTypeName} (Added: {addedCount}, Modified: {modifiedCount}, Removed: {removedCount})");
                    
                    try
                    {
                        try
                        {
                            persistMethodGeneric?.Invoke(this, new object[] { dbSet });
                        }
                        catch (TargetInvocationException tie)
                            when (tie.InnerException is not null)
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
            IEnumerable<string> columnNames= this._connection
                .FetchColumnNames(tableName);

            if (dbSet.ChangeTracker.Added.Any())
            {
                this._connection.InsertEntities(dbSet.ChangeTracker.Added, tableName, columnNames.ToArray());
            }

            IEnumerable<TEntity> modifiedEntities = dbSet
                .ChangeTracker
                .GetModifiedEntities(dbSet);

            if (modifiedEntities.Any())
            {
                this._connection.UpdateEntities(modifiedEntities, tableName, columnNames.ToArray());
            }

            if (dbSet.ChangeTracker.Removed.Count != 0)
            {
                this._connection.DeleteEntities(dbSet.ChangeTracker.Removed, tableName, columnNames.ToArray());
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
            foreach (KeyValuePair<Type, PropertyInfo> dbSetPropertyKvp in this._dbSetProperties)
            {
                Type entityType = dbSetPropertyKvp.Key;
                PropertyInfo dbSetProperty = dbSetPropertyKvp.Value;

                MethodInfo? populateDbSetMethodGeneric = typeof(DbContext)
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
            IEnumerable<TEntity> tableEntities = this.LoadTableEntities<TEntity>();
            DbSet<TEntity> dbSetInstance = new DbSet<TEntity>(tableEntities);
            
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
                .Select(pi => pi.Name)
                .ToArray();
            
            return entityColumnNames;
        }
        
        private string GetTableName(Type entityType)
        {
            string? tableName = entityType.GetCustomAttribute<TableAttribute>()?.Name;

            if (tableName is null)
            {
                tableName = this._dbSetProperties[entityType].Name;
            }

            return tableName;
        }
        
        private void MapAllRelations()
        {
            foreach (KeyValuePair<Type, PropertyInfo> dbSetPropertyKvp in this._dbSetProperties)
            {
                Type entityType = dbSetPropertyKvp.Key;
                object? dbSetInstance = dbSetPropertyKvp.Value.GetValue(this);
                
                if (dbSetInstance is null)
                {
                    throw new InvalidOperationException(NullDbSetMessage);
                }
                
                MethodInfo? mapRelationsMethodGeneric = typeof(DbContext)
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

            PropertyInfo[] navigationCollections = entityType
                .GetProperties()
                .Where(pi => pi.PropertyType.IsGenericType &&
                             pi.PropertyType.GetGenericTypeDefinition() == typeof(ICollection<>) &&
                             this._dbSetProperties.ContainsKey(pi.PropertyType.GetGenericArguments().First()))
                .ToArray();

            foreach (PropertyInfo navigationCollectionPropertyInfo in navigationCollections)
            {
                Type collectionEntityType = navigationCollectionPropertyInfo
                    .PropertyType
                    .GenericTypeArguments
                    .First();
                
                MethodInfo? mapCollectionMethodGeneric = typeof(DbContext)
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

            PropertyInfo[] foreignKeyProperties = entityType
                .GetProperties()
                .Where(pi => pi.HasAttribute<ForeignKeyAttribute>())
                .ToArray();

            foreach (PropertyInfo foreignKeyPropertyInfo in foreignKeyProperties)
            {
                string navigationPropertyName = foreignKeyPropertyInfo
                    .GetCustomAttribute<ForeignKeyAttribute>()!.Name;

                PropertyInfo navigationPropertyInfo = entityType
                    .GetProperties()
                    .First(pi => pi.Name == navigationPropertyName);

                object? navigationDbSetInstance = this._dbSetProperties[navigationPropertyInfo.PropertyType]
                    .GetValue(this);

                if (navigationDbSetInstance is null)
                {
                    throw new InvalidOperationException(NavPropertyWithoutDbSetMessage);
                }
                
                PropertyInfo navigationPrimaryKey = navigationPropertyInfo
                    .PropertyType
                    .GetProperties()
                    .First(pi => pi.HasAttribute<KeyAttribute>());

                foreach (TEntity entity in dbSetInstance)
                {
                    object? foreignKeyValue = foreignKeyPropertyInfo.GetValue(entity);
                    if (foreignKeyValue is null)
                        continue;

                    object navigationEntity = ((IEnumerable<object>)navigationDbSetInstance)
                        .First(currNavPropEntity => navigationPrimaryKey
                            .GetValue(currNavPropEntity)!
                            .Equals(foreignKeyValue));
                    
                    navigationPropertyInfo.SetValue(entity, navigationEntity);
                }
            }
        }
        
        private void MapNavigationCollection<TEntity, TCollectionEntity>(DbSet<TEntity> dbSetInstance, PropertyInfo navigationCollectionPropertyInfo)
        where TEntity : class, new()
        where TCollectionEntity : class, new()
        {
            Type entityType = typeof(TEntity);
            Type collectionEntityType = typeof(TCollectionEntity);

            PropertyInfo primaryKey = entityType
                .GetProperties()
                .First(pi => pi.HasAttribute<KeyAttribute>());

            PropertyInfo foreignKey = collectionEntityType
                .GetProperties()
                .First(pi => pi.HasAttribute<ForeignKeyAttribute>() &&
                             collectionEntityType
                                 .GetProperty(pi.GetCustomAttribute<ForeignKeyAttribute>()!.Name)!
                                 .PropertyType == entityType);

            DbSet<TCollectionEntity>? navCollectionDbSet = (DbSet<TCollectionEntity>?)
                this._dbSetProperties[collectionEntityType]
                .GetValue(this);

            if (navCollectionDbSet is null)
            {
                throw new InvalidOperationException(NullDbSetMessage);
            }

            foreach (TEntity entity in dbSetInstance)
            {
                object? entityPrimaryKeyValue = primaryKey.GetValue(entity);
                ICollection<TCollectionEntity> navigationEntities = navCollectionDbSet
                    .Where(ne => foreignKey.GetValue(ne)?
                        .Equals(entityPrimaryKeyValue) ?? false)
                    .ToArray();
                
                ReflectionHelper.ReplaceBackingField(entity, navigationCollectionPropertyInfo.Name, navigationEntities);
            }
        }
    }
}
