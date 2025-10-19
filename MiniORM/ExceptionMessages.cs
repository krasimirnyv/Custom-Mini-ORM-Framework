namespace MiniORM
{
    internal static class ExceptionMessages
    {
        internal const string PrimaryKeyNullErrorMessage = 
            @"The PK property {0} cannot be null!";
        
        internal const string EntityNullErrorMessage =
            @"The entity cannot be null!";
        
        internal const string PopulateDbSetNotFoundMessage =
            @"There was an internal error while trying to populate the DbSet. Please make sure that your AppDbContext inherits from the MiniORM DbContext class!";
        
        internal const string NullDbSetMessage =
            @"There was an internal error while trying to populate the {0} DbSet.";
        
        internal const string NullCollectionMessage =
            @"There was an internal error while trying to populate the navigation collection {0} of entity {1}.";
        
        internal const string MapRelationsNotFoundMessage =
            @"There was an internal error while trying to map relations. Please make sure that your AppDbContext inherits from the MiniORM DbContext class!";
        
        internal const string MapNavigationCollectionNotFoundMessage =
            @"There was an internal error while trying to map navigation collections. Please make sure that your AppDbContext inherits from the MiniORM DbContext class!";
        
        internal const string InvalidEntitiesInDbSetMessage =
            @"{0} Invalid Entities Found in {1}!";
        
        internal const string TransactionRollbackMessage =
            @"Performing Rollback due to Exception!!!";
        
        internal const string TransactionExceptionMessage =
            @"The SQL Transaction failed due to unexpected error!";
        
        internal const string ConnectionIsNullMessage =
            @"The database connection is null!";
        
        internal const string NoTableNameFound =
            @"Could not find a valid table name for DbSet {0}!";
        
        internal const string InvalidNavigationPropertyName =
            @"Foreign key {0} references navigation property with name {1} which does not exist!";
        
        internal const string NavPropertyWithoutDbSetMessage =
            @"DbSet could not be found for navigation property {0} of type {1}!";
    }
}