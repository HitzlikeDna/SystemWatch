namespace SystemWatch.Monitoring
{
    public class SystemStats
    {
        public double CpuPercent { get; set; }
        public double RamPercent { get; set; }
        public double? GpuPercent { get; set; }
        public double NetKiloBytesPerSecond { get; set; }
        public double DiskMegaBytesPerSecond { get; set; }

        public string DriveName { get; set; } = "C:\\";
        public double DriveTotalGb { get; set; }
        public double DriveFreeGb { get; set; }
        public double DriveUsedPercent { get; set; }
        public bool DriveReady { get; set; }
        public bool DriveError { get; set; }

        public bool NetworkAdapterAvailable { get; set; }
        public bool GpuAvailable => GpuPercent.HasValue;
    }
}
