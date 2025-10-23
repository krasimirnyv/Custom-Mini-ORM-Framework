namespace MiniORM
{
    using System.Collections;

    using static ExceptionMessages;
    
    /// <summary>
    /// Represents a table in the database and is created within a <c>DbContext</c>.
    /// A <c>DbSet</c> exposes a local collection of entities that is initialized 
    /// based on the objects stored in the database.
    /// 
    /// The local collection goes through changes during the application's lifetime. 
    /// When changes are persisted, the local collection of entities is compared 
    /// against the proxy entities tracked in the <c>ChangeTracker</c>.
    /// </summary>
    public class DbSet<TEntity> : ICollection<TEntity>
        where TEntity : class, new()
    {
        internal DbSet(IEnumerable<TEntity> entities)
        {
            this.Entities = entities.ToList();
            this.ChangeTracker = new ChangeTracker<TEntity>(entities);
        }

        public ChangeTracker<TEntity> ChangeTracker { get; }
        internal ICollection<TEntity> Entities { get; }
        
        public int Count => this.Entities.Count;
        
        public bool IsReadOnly => this.Entities.IsReadOnly;
        
        public void Add(TEntity? entity)
        {
            if (entity is null)
            {
                throw new ArgumentNullException(nameof(entity), EntityNullErrorMessage);
            }
            
            // Add the entity to the collection of entities
            this.Entities.Add(entity);
            // Track the added entity in the ChangeTracker
            this.ChangeTracker.Add(entity);
        }

        public bool Remove(TEntity? entity)
        {
            if (entity is null)
            {
                throw new ArgumentNullException(nameof(entity), EntityNullErrorMessage);
            }
            
            // Remove the entity from the collection of entities
            bool isRemoved = this.Entities.Remove(entity);
            if (isRemoved)
            {
                // Track the removed entity in the ChangeTracker
                this.ChangeTracker.Remove(entity);
            }
            
            return isRemoved;
        }

        public bool RemoveRange(IEnumerable<TEntity> entities)
        {
            if (entities is null)
            {
                throw new ArgumentNullException(nameof(entities), EntityNullErrorMessage);
            }

            bool result = true;
            foreach (TEntity entity in entities)
            {
                result &= this.Remove(entity);
            }
            
            return result;
        }
        
        public void Clear()
        {
            while (this.Entities.Count > 0)
            {
                this.Remove(this.Entities.First());
            }
        }

        public bool Contains(TEntity entity)
            => this.Entities.Contains(entity);

        public void CopyTo(TEntity[] array, int arrayIndex)
            => this.Entities.CopyTo(array, arrayIndex);

        public IEnumerator<TEntity> GetEnumerator()
            => this.Entities.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}
