IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'EtimsSettings')
CREATE TABLE EtimsSettings (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    ClientCode NVARCHAR(50) NOT NULL,
    TinPin NVARCHAR(20) NOT NULL,
    BranchId NVARCHAR(10) DEFAULT '00',
    DeviceSerialNo NVARCHAR(100),
    ApiUsername NVARCHAR(100),
    ApiPassword NVARCHAR(200),
    Environment NVARCHAR(20) DEFAULT 'Sandbox',
    IsActive BIT DEFAULT 1,
    CreatedAt DATETIME2 DEFAULT GETUTCDATE()
);
