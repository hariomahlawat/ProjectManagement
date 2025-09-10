using System;

namespace ProjectManagement.Models
{
    public class DailyLoginStat
    {
        public int Id { get; set; }
        public DateOnly Date { get; set; }
        public int Count { get; set; }
    }
}
