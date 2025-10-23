namespace MiniORM
{
    using System.ComponentModel.DataAnnotations;
    using System.Reflection;
    
    using static ExceptionMessages;
    
    /// <summary>
    /// The sole responsibility of this class is to track changes in entities.
    /// It monitors:
    /// - Modified entities in the database (changed property values).
    /// - Newly added entities (stored in the <c>Added</c> collection).
    /// - Removed entities (stored in the <c>Removed</c> collection).
    ///
    /// After persistence:
    /// - The <c>Added</c> and <c>Removed</c> collections are cleared.
    /// - The <c>AllEntities</c> collection is refreshed with the current state from the database.
    /// </summary>
    public class ChangeTracker<TEntity>
        where TEntity : class, new()
    {
        private readonly ICollection<TEntity> _allEntities;  // Tracks updates of the entities
        private readonly ICollection<TEntity> _added; // Tracks added entities (to be added)
        private readonly ICollection<TEntity> _removed; // Tracks removed entities (to be removed)

        public ChangeTracker(IEnumerable<TEntity> entities)
        {
            this._added = new List<TEntity>();
            this._removed = new List<TEntity>();
            this._allEntities = CloneEntities(entities);
        }

        /// <summary>
        /// Keeps track of all existing entities in the DB.
        /// </summary>
        public IReadOnlyCollection<TEntity> AllEntities => (IReadOnlyCollection<TEntity>) this._allEntities;
        
        /// <summary>
        /// Keeps track of the added entities which are not yet persisted in the DB.
        /// </summary>
        public IReadOnlyCollection<TEntity> Added => (IReadOnlyCollection<TEntity>) this._added;

        /// <summary>
        /// Keeps track of the removed entities which are not yet persisted in the DB.
        /// </summary>
        public IReadOnlyCollection<TEntity> Removed => (IReadOnlyCollection<TEntity>)this._removed;
        
        /// <summary>
        /// Marks the given entity as added in _added collection.
        /// </summary>
        /// <param name="entity">Entity to be inserted.</param>
        public void Add(TEntity entity) => this._added.Add(entity);

        /// <summary>
        /// Marks the given entity as removed in _removed collection.
        /// </summary>
        /// <param name="entity">Entity to be deleted.</param>
        public void Remove(TEntity entity) => this._removed.Add(entity);

        public IEnumerable<TEntity> GetModifiedEntities(DbSet<TEntity> dbSet)
        {
            ICollection<TEntity> modifiedEntities = new HashSet<TEntity>();
            
            // Usually will be an array with single property inside - the PK
            // But may be an array of several properties - composite PK
            PropertyInfo[] primaryKeys = typeof(TEntity)
                .GetProperties()
                .Where(pi => pi.HasAttribute<KeyAttribute>())
                .ToArray();

            foreach (TEntity proxyEntity in this.AllEntities)
            {
                IEnumerable<object> primaryKeyValues = GetPrimaryKeyValues(proxyEntity, primaryKeys);
                
                TEntity localEntity = dbSet.Entities
                    .Single(le => GetPrimaryKeyValues(le, primaryKeys).SequenceEqual(primaryKeyValues));
                
                bool isModified = IsModified(proxyEntity, localEntity);
                if (isModified)
                    modifiedEntities.Add(localEntity);
            }

            return modifiedEntities;
        }
        
        /// <summary>
        /// Performs shallow copy of the entities /collection of a reference type/ to be tracked.
        /// </summary>
        /// <param name="entities">Collection to be cloned.</param>
        /// <returns>Cloned collection.</returns>
        private static ICollection<TEntity> CloneEntities(IEnumerable<TEntity> entities)
        {
            ICollection<TEntity> clonedEntities = new List<TEntity>();

            PropertyInfo[] propertiesToClone = typeof(TEntity)
                .GetProperties()
                .Where(pi => DbContext.AllowedSqlTypes.Contains(pi.PropertyType))
                .ToArray();

            foreach (TEntity entity in entities)
            {
                TEntity clonedEntity = Activator.CreateInstance<TEntity>();
                foreach (PropertyInfo property in propertiesToClone)
                {
                    object? propertyValue = property.GetValue(entity);
                    property.SetValue(clonedEntity, propertyValue);
                }
                
                // clonedEntities.Add(clonedEntity);
            }

            return clonedEntities;
        }
        
        private static IEnumerable<object> GetPrimaryKeyValues(TEntity entity, PropertyInfo[] primaryKeys)
        {
            return primaryKeys
                .Select(pk => pk.GetValue(entity))!;
        }
        
        private static bool IsModified(TEntity proxyEntity, TEntity localEntity)
        {
            PropertyInfo[] trackedProperties = typeof(TEntity)
                .GetProperties()
                .Where(pi => DbContext.AllowedSqlTypes.Contains(pi.PropertyType))
                .ToArray();

            return trackedProperties
                .Any(pi => !Equals(pi.GetValue(proxyEntity), pi.GetValue(localEntity)));
        }
    }
}