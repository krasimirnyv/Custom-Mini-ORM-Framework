namespace MiniORM.App
{
    using Data;
    using Data.Entities;

    class Program
    {
        static void Main(string[] args)
        {
            string connectionString =
                @"Server=127.0.0.1,1433;" +
                @"Database=MiniORM;" +
                @"User Id=sa;" +
                @"Password=Krasi1828;" +
                @"Encrypt=False;" +
                @"TrustServerCertificate=True;";

            MyDbContext context = new MyDbContext(connectionString);

            Console.WriteLine("Entities mapped successfully!");

            context.Employees.Add(new Employee
            {
                FirstName = "Krasi",
                MiddleName = "Krasimirov",
                LastName = "Naydenov",
                IsEmployed = true,
                DepartmentId = context.Departments.First().Id
            });

            Employee employee = context.Employees.Last();
            employee.FirstName = "Krasimir";

            context.SaveChanges();

            Console.WriteLine(context.Employees.Last().Id);
            Console.WriteLine(context.Employees.Last().FirstName);
            Console.WriteLine(context.Employees.Last().MiddleName);
            Console.WriteLine(context.Employees.Last().LastName);
            Console.WriteLine(context.Employees.Last().DepartmentId);
            Console.WriteLine(context.Employees.Last().IsEmployed);
            Console.WriteLine(context.Employees.Last().EmployeeProjects.Count);
        }
    }
}