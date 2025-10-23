namespace MiniORM.App.Data.Entities
{
    using System.ComponentModel.DataAnnotations;

    public class Project
    {
        public Project()
        {
            EmployeeProjects = new HashSet<EmployeesProject>();
        }
        
        [Key]
        public int Id { get; set; }

        [Required]
        public string Name { get; set; }
        
        public ICollection<EmployeesProject> EmployeeProjects { get; }
    }
}
