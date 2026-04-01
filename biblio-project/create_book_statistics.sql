CREATE TABLE dbo.BookStatistics (
    Id INT PRIMARY KEY IDENTITY(1,1),
    BookId INT NOT NULL UNIQUE,
    TotalLoansCount INT NOT NULL DEFAULT 0,
    CurrentActiveLoansCount INT NOT NULL DEFAULT 0,
    TotalReservationsCount INT NOT NULL DEFAULT 0,
    CurrentActiveReservationsCount INT NOT NULL DEFAULT 0,
    LastLoanDate DATETIME NULL,
    LastReservationDate DATETIME NULL,
    UpdatedAt DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT FK_BookStatistics_Books FOREIGN KEY (BookId)
        REFERENCES dbo.Books(Id) ON DELETE CASCADE
);
GO
