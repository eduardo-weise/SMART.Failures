using System.Management;
using System.Runtime.InteropServices;
using Vanara.PInvoke;

namespace SMARTInfo
{
    class Program
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct ATA_SMART_ATTRIBUTE
        {
            public byte Id;
            public short StatusFlags;
            public byte Value;
            public byte Worst;
            public byte RawValue0;
            public byte RawValue1;
            public byte RawValue2;
            public byte RawValue3;
            public byte RawValue4;
            public byte RawValue5;
            public byte Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct STORAGE_PROPERTY_QUERY
        {
            public STORAGE_PROPERTY_ID PropertyId;
            public STORAGE_QUERY_TYPE QueryType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            public byte[] AdditionalParameters;
        }

        public enum STORAGE_PROPERTY_ID
        {
            StorageDeviceProperty = 0
        }

        public enum STORAGE_QUERY_TYPE
        {
            PropertyStandardQuery = 0
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct STORAGE_DEVICE_DESCRIPTOR
        {
            public uint Version;
            public uint Size;
            public byte DeviceType;
            public byte DeviceTypeModifier;
            public bool RemovableMedia;
            public bool CommandQueueing;
            public uint VendorIdOffset;
            public uint ProductIdOffset;
            public uint ProductRevisionOffset;
            public uint SerialNumberOffset;
            public STORAGE_BUS_TYPE BusType;
            public uint RawPropertiesLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            public byte[] RawDeviceProperties;
        }

        public enum STORAGE_BUS_TYPE
        {
            BusTypeUnknown = 0x00,
            BusTypeScsi = 0x1,
            BusTypeAtapi = 0x2,
            BusTypeAta = 0x3,
            BusType1394 = 0x4,
            BusTypeSsa = 0x5,
            BusTypeFibre = 0x6,
            BusTypeUsb = 0x7,
            BusTypeRAID = 0x8,
            BusTypeiScsi = 0x9,
            BusTypeSas = 0xA,
            BusTypeSata = 0xB,
            BusTypeSd = 0xC,
            BusTypeMmc = 0xD,
            BusTypeVirtual = 0xE,
            BusTypeFileBackedVirtual = 0xF,
            BusTypeSpaces = 0x10,
            BusTypeNvme = 0x11,
            BusTypeSCM = 0x12,
            BusTypeUfs = 0x13,
            BusTypeMax = 0x14,
            BusTypeMaxReserved = 0x7F
        }

        public const byte IDE_COMMAND_ID_ATA = 0xEC;

        static void Main(string[] args)
        {
            var physicalDrives = GetPhysicalDrivePaths();

            if (physicalDrives.Count == 0)
            {
                Console.WriteLine("No physical drives found.");
                return;
            }

            Console.WriteLine("Physical Drives:");

            foreach (var drive in physicalDrives)
            {
                Console.WriteLine(drive);
            }

            // Exemplo: Vamos capturar o PhysicalDrive0
            string driveToCapture = physicalDrives.FirstOrDefault(d => d.EndsWith("PhysicalDrive0", StringComparison.OrdinalIgnoreCase));

            if (driveToCapture != null)
            {
                Console.WriteLine($"\nCaptured drive: {driveToCapture}");
                // Chame a função para obter os dados SMART aqui
                GetSmartData(driveToCapture);
            }
            else
            {
                Console.WriteLine("\nPhysicalDrive0 not found.");
            }

            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        public static List<string> GetPhysicalDrivePaths()
        {
            var result = new List<string>();

            using (var searcher = new ManagementObjectSearcher("SELECT DeviceID FROM Win32_DiskDrive"))
            {
                foreach (ManagementObject disk in searcher.Get())
                {
                    string deviceId = disk["DeviceID"]?.ToString();
                    if (!string.IsNullOrEmpty(deviceId))
                    {
                        result.Add(@"\\.\PHYSICALDRIVE" + deviceId.Substring(deviceId.Length - 1));
                    }
                }
            }

            return result;
        }

        public static void GetSmartData(string drive)
        {
            using (var hDevice = Kernel32.CreateFile(drive, Kernel32.FileAccess.GENERIC_READ | Kernel32.FileAccess.GENERIC_WRITE, 0, null, FileMode.Open, 0, IntPtr.Zero))
            {
                if (hDevice.IsInvalid)
                {
                    Console.WriteLine("Failed to access the drive.");
                    return;
                }

                var command = new STORAGE_PROPERTY_QUERY
                {
                    PropertyId = STORAGE_PROPERTY_ID.StorageDeviceProperty,
                    QueryType = STORAGE_QUERY_TYPE.PropertyStandardQuery,
                    AdditionalParameters = new byte[1]
                };

                int bufferSize = Marshal.SizeOf<STORAGE_DEVICE_DESCRIPTOR>() + 512;
                IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
                IntPtr commandPtr = Marshal.AllocHGlobal(Marshal.SizeOf(command));
                Marshal.StructureToPtr(command, commandPtr, false);

                try
                {
                    if (!Kernel32.DeviceIoControl(hDevice, Kernel32.IOControlCode.IOCTL_STORAGE_QUERY_PROPERTY, commandPtr, (uint)Marshal.SizeOf(command), buffer, (uint)bufferSize, out _, IntPtr.Zero))
                    {
                        int error = Marshal.GetLastWin32Error();
                        Console.WriteLine($"Failed to query drive properties. Error code: {error}");
                        return;
                    }

                    var descriptor = Marshal.PtrToStructure<STORAGE_DEVICE_DESCRIPTOR>(buffer);
                    IntPtr offsetPtr = buffer + (int)descriptor.ProductIdOffset;
                    string productId = Marshal.PtrToStringAnsi(offsetPtr);
                    Console.WriteLine($"Drive: {productId}");

                    // Build SMART data retrieval structure
                    var idCmd = new SENDCMDINPARAMS
                    {
                        cBufferSize = (uint)Marshal.SizeOf(typeof(IDEREGS)),
                        irDriveRegs = new IDEREGS
                        {
                            bCommandReg = IDE_COMMAND_ID_ATA,
                            bSectorCountReg = 1,
                            bSectorNumberReg = 1
                        },
                        bDriveNumber = new byte[] { (byte)int.Parse(drive.Substring(drive.Length - 1)) },
                        dwReserved = new uint[4],
                        bBuffer = new byte[512] // Tamanho do buffer ajustado conforme necessário
                    };

                    IntPtr idCmdPtr = Marshal.AllocHGlobal(Marshal.SizeOf(idCmd));
                    Marshal.StructureToPtr(idCmd, idCmdPtr, false);

                    int outCmdSize = Marshal.SizeOf<SENDCMDOUTPARAMS>();
                    IntPtr outCmdPtr = Marshal.AllocHGlobal(outCmdSize);

                    if (!Kernel32.DeviceIoControl(hDevice, Kernel32.IOControlCode.SMART_RCV_DRIVE_DATA, idCmdPtr, (uint)Marshal.SizeOf(idCmd), outCmdPtr, (uint)outCmdSize, out _, IntPtr.Zero))
                    {
                        int error = Marshal.GetLastWin32Error();
                        Console.WriteLine($"Failed to retrieve SMART data. Error code: {error}");
                        return;
                    }

                    var outCmd = Marshal.PtrToStructure<SENDCMDOUTPARAMS>(outCmdPtr);
                    var smartData = outCmd.bBuffer;

                    // Exemplo de leitura dos atributos SMART
                    for (int i = 2; i < 512; i += Marshal.SizeOf(typeof(ATA_SMART_ATTRIBUTE)))
                    {
                        var attribute = MemoryMarshal.Read<ATA_SMART_ATTRIBUTE>(smartData.AsSpan(i, Marshal.SizeOf(typeof(ATA_SMART_ATTRIBUTE))));
                        Console.WriteLine($"ID: {attribute.Id}, Value: {attribute.Value}, Worst: {attribute.Worst}");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                    Marshal.FreeHGlobal(commandPtr);
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SENDCMDINPARAMS
    {
        public uint cBufferSize;
        public IDEREGS irDriveRegs;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public byte[] bDriveNumber;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public uint[] dwReserved;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public byte[] bBuffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SENDCMDOUTPARAMS
    {
        public uint cBufferSize;
        public DRIVERSTATUS DriverStatus;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 512)]
        public byte[] bBuffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DRIVERSTATUS
    {
        public byte bDriverError;
        public byte bIDEStatus;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] bReserved;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public uint[] dwReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IDEREGS
    {
        public byte bFeaturesReg;
        public byte bSectorCountReg;
        public byte bSectorNumberReg;
        public byte bCylLowReg;
        public byte bCylHighReg;
        public byte bDriveHeadReg;
        public byte bCommandReg;
        public byte bReserved;
    }
}
