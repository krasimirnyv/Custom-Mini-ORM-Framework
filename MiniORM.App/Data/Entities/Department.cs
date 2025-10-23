namespace MiniORM.App.Data.Entities
{
    using System.ComponentModel.DataAnnotations;

    public class Department
    {
        public Department()
        {
            Employees = new HashSet<Employee>();
        }

        [Key] 
        public int Id { get; set; }

        [Required]
        public string Name { get; set; }

        public ICollection<Employee> Employees { get; }
    }
}
