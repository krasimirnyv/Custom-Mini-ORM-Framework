namespace MiniORM.App.Data
{
    using MiniORM.App.Data.Entities;

    public class MyDbContext : DbContext
    {
        public MyDbContext(string connectionString)
            : base(connectionString)
        {
        }
        
        public DbSet<Department> Departments { get; set; }
        
        public DbSet<Project> Projects { get; set; }
        
        public DbSet<Employee> Employees { get; set; }
        
        public DbSet<EmployeesProject> EmployeesProjects { get; set; }
    }
}
