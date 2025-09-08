using System;

namespace ProjectManagement.Services
{
    public sealed class UserLifecycleOptions
    {
        public int HardDeleteWindowHours { get; set; } = 72;
        public int UndoWindowMinutes { get; set; } = 15;
    }
}
