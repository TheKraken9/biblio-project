-- =============================================
-- Script de création de la base de données
-- Système de gestion de bibliothèque
-- =============================================

USE master;
GO

-- Créer la base de données si elle n'existe pas
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'LibraryManagementDB')
BEGIN
    CREATE DATABASE LibraryManagementDB;
END
GO

USE LibraryManagementDB;
GO

-- =============================================
-- 1. Table LibraryUser
-- =============================================
IF OBJECT_ID('dbo.LibraryUser', 'U') IS NOT NULL
DROP TABLE dbo.LibraryUser;
GO

CREATE TABLE dbo.LibraryUser (
                                 Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                                 Username NVARCHAR(100) NOT NULL UNIQUE,
                                 Email NVARCHAR(255) NOT NULL UNIQUE,
                                 PasswordHash NVARCHAR(255) NOT NULL,
                                 FirstName NVARCHAR(100) NOT NULL,
                                 LastName NVARCHAR(100) NOT NULL,
                                 PhoneNumber NVARCHAR(30) NULL,
                                 IsActive BIT NOT NULL DEFAULT 1,
                                 RegistrationDate DATETIME NOT NULL DEFAULT GETDATE(),
                                 CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),
                                 UpdatedAt DATETIME NOT NULL DEFAULT GETDATE(),
);
GO

-- Index sur Email pour recherche rapide
CREATE NONCLUSTERED INDEX IX_LibraryUser_Email
ON dbo.LibraryUser(Email);
GO

-- =============================================
-- 2. Table Role
-- =============================================
IF OBJECT_ID('dbo.Role', 'U') IS NOT NULL
DROP TABLE dbo.Role;
GO

CREATE TABLE dbo.Role (
                          Id INT PRIMARY KEY IDENTITY(1,1),
                          Name NVARCHAR(50) NOT NULL UNIQUE,
                          Description NVARCHAR(255) NULL
);
GO

-- =============================================
-- 3. Table UserRole (jointure)
-- =============================================
IF OBJECT_ID('dbo.UserRole', 'U') IS NOT NULL
DROP TABLE dbo.UserRole;
GO

CREATE TABLE dbo.UserRole (
                              UserId UNIQUEIDENTIFIER NOT NULL,
                              RoleId INT NOT NULL,
                              CONSTRAINT PK_UserRole PRIMARY KEY (UserId, RoleId),
                              CONSTRAINT FK_UserRole_LibraryUser FOREIGN KEY (UserId)
                                  REFERENCES dbo.LibraryUser(Id) ON DELETE CASCADE,
                              CONSTRAINT FK_UserRole_Role FOREIGN KEY (RoleId)
                                  REFERENCES dbo.Role(Id) ON DELETE CASCADE
);
GO

-- =============================================
-- 4. Table Author
-- =============================================
IF OBJECT_ID('dbo.Author', 'U') IS NOT NULL
DROP TABLE dbo.Author;
GO

CREATE TABLE dbo.Author (
                            Id INT PRIMARY KEY IDENTITY(1,1),
                            FirstName NVARCHAR(100) NULL,
                            LastName NVARCHAR(100) NOT NULL,
                            BirthYear INT NULL,
                            DeathYear INT NULL,
                            Bio NVARCHAR(MAX) NULL,
                            CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),
                            UpdatedAt DATETIME NOT NULL DEFAULT GETDATE()
);
GO

-- =============================================
-- 5. Table Category
-- =============================================
IF OBJECT_ID('dbo.Category', 'U') IS NOT NULL
DROP TABLE dbo.Category;
GO

CREATE TABLE dbo.Category (
                              Id INT PRIMARY KEY IDENTITY(1,1),
                              Name NVARCHAR(100) NOT NULL UNIQUE,
                              Slug NVARCHAR(100) NOT NULL UNIQUE,
                              Description NVARCHAR(255) NULL,
                              CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),
                              UpdatedAt DATETIME NOT NULL DEFAULT GETDATE()
);
GO

-- =============================================
-- 6. Table Publisher
-- =============================================
IF OBJECT_ID('dbo.Publisher', 'U') IS NOT NULL
DROP TABLE dbo.Publisher;
GO

CREATE TABLE dbo.Publisher (
                               Id INT PRIMARY KEY IDENTITY(1,1),
                               Name NVARCHAR(200) NOT NULL UNIQUE,
                               Country NVARCHAR(100) NULL,
                               City NVARCHAR(100) NULL,
                               Website NVARCHAR(255) NULL,
                               CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),
                               UpdatedAt DATETIME NOT NULL DEFAULT GETDATE()
);
GO

-- =============================================
-- 7. Table Book
-- =============================================
IF OBJECT_ID('dbo.Book', 'U') IS NOT NULL
DROP TABLE dbo.Book;
GO

CREATE TABLE dbo.Book (
                          Id INT PRIMARY KEY IDENTITY(1,1),
                          Title NVARCHAR(255) NOT NULL,
                          NormalizedTitle NVARCHAR(255) NOT NULL,
                          Subtitle NVARCHAR(255) NULL,
                          Summary NVARCHAR(MAX) NULL,
                          PublicationYear INT NULL,
                          PublisherId INT NULL,
                          MainCategoryId INT NULL,
                          CoverImageUrl NVARCHAR(500) NULL,
                          AuthorNamesText NVARCHAR(500) NULL,
                          CategoryNamesText NVARCHAR(500) NULL,
                          Keywords NVARCHAR(500) NULL,
                          AvailableCopiesCount INT NOT NULL DEFAULT 0,
                          TotalCopiesCount INT NOT NULL DEFAULT 0,
                          CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),
                          UpdatedAt DATETIME NOT NULL DEFAULT GETDATE(),
                          CONSTRAINT FK_Book_Publisher FOREIGN KEY (PublisherId)
                              REFERENCES dbo.Publisher(Id) ON DELETE SET NULL,
                          CONSTRAINT FK_Book_MainCategory FOREIGN KEY (MainCategoryId)
                              REFERENCES dbo.Category(Id) ON DELETE SET NULL
);
GO

-- Index pour recherche par titre
CREATE NONCLUSTERED INDEX IX_Book_NormalizedTitle
ON dbo.Book(NormalizedTitle);
GO

-- Index pour recherche full-text (optionnel)
-- CREATE FULLTEXT INDEX ON dbo.Book(Title, Summary, AuthorNamesText, Keywords)
-- KEY INDEX PK_Book;
-- GO

-- =============================================
-- 8. Table BookAuthor (jointure)
-- =============================================
IF OBJECT_ID('dbo.BookAuthor', 'U') IS NOT NULL
DROP TABLE dbo.BookAuthor;
GO

CREATE TABLE dbo.BookAuthor (
                                BookId INT NOT NULL,
                                AuthorId INT NOT NULL,
                                CONSTRAINT PK_BookAuthor PRIMARY KEY (BookId, AuthorId),
                                CONSTRAINT FK_BookAuthor_Book FOREIGN KEY (BookId)
                                    REFERENCES dbo.Book(Id) ON DELETE CASCADE,
                                CONSTRAINT FK_BookAuthor_Author FOREIGN KEY (AuthorId)
                                    REFERENCES dbo.Author(Id) ON DELETE CASCADE
);
GO

-- =============================================
-- 9. Table BookCategory (jointure)
-- =============================================
IF OBJECT_ID('dbo.BookCategory', 'U') IS NOT NULL
DROP TABLE dbo.BookCategory;
GO

CREATE TABLE dbo.BookCategory (
                                  BookId INT NOT NULL,
                                  CategoryId INT NOT NULL,
                                  CONSTRAINT PK_BookCategory PRIMARY KEY (BookId, CategoryId),
                                  CONSTRAINT FK_BookCategory_Book FOREIGN KEY (BookId)
                                      REFERENCES dbo.Book(Id) ON DELETE CASCADE,
                                  CONSTRAINT FK_BookCategory_Category FOREIGN KEY (CategoryId)
                                      REFERENCES dbo.Category(Id) ON DELETE CASCADE
);
GO

-- =============================================
-- 10. Table BookCopy
-- =============================================
IF OBJECT_ID('dbo.BookCopy', 'U') IS NOT NULL
DROP TABLE dbo.BookCopy;
GO

CREATE TABLE dbo.BookCopy (
                              Id INT PRIMARY KEY IDENTITY(1,1),
                              BookId INT NOT NULL,
                              Barcode NVARCHAR(50) NOT NULL UNIQUE,
                              ShelfLocation NVARCHAR(100) NOT NULL,
                              Condition NVARCHAR(50) NOT NULL DEFAULT 'Good',
                              AcquisitionDate DATETIME NOT NULL DEFAULT GETDATE(),
                              Status NVARCHAR(20) NOT NULL DEFAULT 'Disponible',
                              IsReferenceOnly BIT NOT NULL DEFAULT 0,
                              BookTitleSnapshot NVARCHAR(255) NULL,
                              MainCategorySnapshot NVARCHAR(100) NULL,
                              AuthorNamesSnapshot NVARCHAR(255) NULL,
                              CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),
                              UpdatedAt DATETIME NOT NULL DEFAULT GETDATE(),
                              CONSTRAINT FK_BookCopy_Book FOREIGN KEY (BookId)
                                  REFERENCES dbo.Book(Id) ON DELETE CASCADE,
                              CONSTRAINT CK_BookCopy_Status CHECK (Status IN ('Disponible', 'ON_LOAN', 'RESERVED', 'LOST', 'DAMAGED', 'MAINTENANCE'))
);
GO

-- Index pour recherche par livre
CREATE NONCLUSTERED INDEX IX_BookCopy_BookId
ON dbo.BookCopy(BookId);
GO

-- Index pour recherche par statut
CREATE NONCLUSTERED INDEX IX_BookCopy_Status
ON dbo.BookCopy(Status);
GO

-- =============================================
-- 11. Table Loan
-- =============================================
IF OBJECT_ID('dbo.Loan', 'U') IS NOT NULL
DROP TABLE dbo.Loan;
GO

CREATE TABLE dbo.Loan (
                          Id INT PRIMARY KEY IDENTITY(1,1),
                          BookCopyId INT NOT NULL,
                          BorrowerId UNIQUEIDENTIFIER NOT NULL,
                          LoanDate DATETIME NOT NULL DEFAULT GETDATE(),
                          DueDate DATETIME NOT NULL,
                          ReturnDate DATETIME NULL,
                          Status NVARCHAR(20) NOT NULL DEFAULT 'ONGOING',
                          RenewalCount INT NOT NULL DEFAULT 0,
                          LastReminderDate DATETIME NULL,
                          BookId INT NOT NULL,
                          BookTitleSnapshot NVARCHAR(255) NULL,
                          BorrowerNameSnapshot NVARCHAR(200) NULL,
                          BorrowerEmailSnapshot NVARCHAR(255) NULL,
                          CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),
                          UpdatedAt DATETIME NOT NULL DEFAULT GETDATE(),
                          CONSTRAINT FK_Loan_BookCopy FOREIGN KEY (BookCopyId)
                              REFERENCES dbo.BookCopy(Id) ON DELETE CASCADE,
                          CONSTRAINT FK_Loan_Borrower FOREIGN KEY (BorrowerId)
                              REFERENCES dbo.LibraryUser(Id),
                          CONSTRAINT FK_Loan_Book FOREIGN KEY (BookId)
                              REFERENCES dbo.Book(Id),
                          CONSTRAINT CK_Loan_Status CHECK (Status IN ('ONGOING', 'RETURNED', 'LATE', 'LOST'))
);
GO

-- Index pour recherche par exemplaire
CREATE NONCLUSTERED INDEX IX_Loan_BookCopyId
ON dbo.Loan(BookCopyId);
GO

-- Index pour recherche par emprunteur
CREATE NONCLUSTERED INDEX IX_Loan_BorrowerId
ON dbo.Loan(BorrowerId);
GO

-- Index pour recherche par statut et date limite
CREATE NONCLUSTERED INDEX IX_Loan_Status_DueDate
ON dbo.Loan(Status, DueDate);
GO

-- Index pour stats par livre
CREATE NONCLUSTERED INDEX IX_Loan_BookId
ON dbo.Loan(BookId);
GO

-- =============================================
-- 12. Table Reservation
-- =============================================
IF OBJECT_ID('dbo.Reservation', 'U') IS NOT NULL
DROP TABLE dbo.Reservation;
GO

CREATE TABLE dbo.Reservation (
                                 Id INT PRIMARY KEY IDENTITY(1,1),
                                 BookId INT NOT NULL,
                                 RequesterId UNIQUEIDENTIFIER NOT NULL,
                                 AssignedCopyId INT NULL,
                                 Status NVARCHAR(20) NOT NULL DEFAULT 'PENDING',
                                 PositionInQueue INT NOT NULL,
                                 RequestedAt DATETIME NOT NULL DEFAULT GETDATE(),
                                 ExpiresAt DATETIME NULL,
                                 BookTitleSnapshot NVARCHAR(255) NULL,
                                 RequesterNameSnapshot NVARCHAR(200) NULL,
                                 CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),
                                 UpdatedAt DATETIME NOT NULL DEFAULT GETDATE(),
                                 CONSTRAINT FK_Reservation_Book FOREIGN KEY (BookId)
                                     REFERENCES dbo.Book(Id) ON DELETE CASCADE,
                                 CONSTRAINT FK_Reservation_Requester FOREIGN KEY (RequesterId)
                                     REFERENCES dbo.LibraryUser(Id),
                                 CONSTRAINT FK_Reservation_AssignedCopy FOREIGN KEY (AssignedCopyId)
                                     REFERENCES dbo.BookCopy(Id),
                                 CONSTRAINT CK_Reservation_Status CHECK (Status IN ('PENDING', 'READY_FOR_PICKUP', 'CANCELLED', 'EXPIRED', 'FULFILLED'))
);
GO

-- Index pour recherche par livre et statut
CREATE NONCLUSTERED INDEX IX_Reservation_BookId_Status
ON dbo.Reservation(BookId, Status);
GO

-- Index pour recherche par demandeur
CREATE NONCLUSTERED INDEX IX_Reservation_RequesterId
ON dbo.Reservation(RequesterId);
GO

-- =============================================
-- 13. Table Notification
-- =============================================
IF OBJECT_ID('dbo.Notification', 'U') IS NOT NULL
DROP TABLE dbo.Notification;
GO

CREATE TABLE dbo.Notification (
                                  Id INT PRIMARY KEY IDENTITY(1,1),
                                  UserId UNIQUEIDENTIFIER NOT NULL,
                                  Type NVARCHAR(50) NOT NULL,
                                  Title NVARCHAR(255) NOT NULL,
                                  Message NVARCHAR(MAX) NOT NULL,
                                  RelatedLoanId INT NULL,
                                  RelatedReservationId INT NULL,
                                  IsRead BIT NOT NULL DEFAULT 0,
                                  SentAt DATETIME NOT NULL DEFAULT GETDATE(),
                                  ReadAt DATETIME NULL,
                                  Channel NVARCHAR(20) NOT NULL DEFAULT 'IN_APP',
                                  CONSTRAINT FK_Notification_User FOREIGN KEY (UserId)
                                      REFERENCES dbo.LibraryUser(Id) ON DELETE CASCADE,
                                  CONSTRAINT FK_Notification_Loan FOREIGN KEY (RelatedLoanId)
                                      REFERENCES dbo.Loan(Id),
                                  CONSTRAINT FK_Notification_Reservation FOREIGN KEY (RelatedReservationId)
                                      REFERENCES dbo.Reservation(Id),
                                  CONSTRAINT CK_Notification_Type CHECK (Type IN ('LOAN_OVERDUE', 'RESERVATION_READY', 'LOAN_DUE_SOON', 'FINE_ISSUED', 'GENERAL')),
                                  CONSTRAINT CK_Notification_Channel CHECK (Channel IN ('EMAIL', 'IN_APP', 'SMS'))
);
GO

-- Index pour recherche par utilisateur
CREATE NONCLUSTERED INDEX IX_Notification_UserId_IsRead
ON dbo.Notification(UserId, IsRead);
GO

-- =============================================
-- 14. Table Fine
-- =============================================
IF OBJECT_ID('dbo.Fine', 'U') IS NOT NULL
DROP TABLE dbo.Fine;
GO

CREATE TABLE dbo.Fine (
                          Id INT PRIMARY KEY IDENTITY(1,1),
                          UserId UNIQUEIDENTIFIER NOT NULL,
                          LoanId INT NULL,
                          Amount DECIMAL(10,2) NOT NULL,
                          Reason NVARCHAR(255) NOT NULL,
                          Status NVARCHAR(20) NOT NULL DEFAULT 'PENDING',
                          CreatedAt DATETIME NOT NULL DEFAULT GETDATE(),
                          PaidAt DATETIME NULL,
                          CONSTRAINT FK_Fine_User FOREIGN KEY (UserId)
                              REFERENCES dbo.LibraryUser(Id),
                          CONSTRAINT FK_Fine_Loan FOREIGN KEY (LoanId)
                              REFERENCES dbo.Loan(Id),
                          CONSTRAINT CK_Fine_Status CHECK (Status IN ('PENDING', 'PAID', 'CANCELLED')),
                          CONSTRAINT CK_Fine_Amount CHECK (Amount >= 0)
);
GO

-- Index pour recherche par utilisateur et statut
CREATE NONCLUSTERED INDEX IX_Fine_UserId_Status
ON dbo.Fine(UserId, Status);
GO

-- =============================================
-- 15. Table BookStatistics
-- =============================================
IF OBJECT_ID('dbo.BookStatistics', 'U') IS NOT NULL
DROP TABLE dbo.BookStatistics;
GO

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
                                    CONSTRAINT FK_BookStatistics_Book FOREIGN KEY (BookId)
                                        REFERENCES dbo.Book(Id) ON DELETE CASCADE
);
GO

-- =============================================
-- 16. Table SearchIndexLog
-- =============================================
IF OBJECT_ID('dbo.SearchIndexLog', 'U') IS NOT NULL
DROP TABLE dbo.SearchIndexLog;
GO

CREATE TABLE dbo.SearchIndexLog (
                                    Id INT PRIMARY KEY IDENTITY(1,1),
                                    EntityType NVARCHAR(50) NOT NULL,
                                    EntityId INT NOT NULL,
                                    Operation NVARCHAR(10) NOT NULL,
                                    PayloadJson NVARCHAR(MAX) NULL,
                                    IndexedAt DATETIME NOT NULL DEFAULT GETDATE(),
                                    Status NVARCHAR(20) NOT NULL DEFAULT 'SUCCESS',
                                    ErrorMessage NVARCHAR(MAX) NULL,
                                    CONSTRAINT CK_SearchIndexLog_EntityType CHECK (EntityType IN ('BOOK', 'AUTHOR', 'CATEGORY', 'USER')),
                                    CONSTRAINT CK_SearchIndexLog_Operation CHECK (Operation IN ('INDEX', 'DELETE', 'UPDATE')),
                                    CONSTRAINT CK_SearchIndexLog_Status CHECK (Status IN ('SUCCESS', 'ERROR', 'PENDING'))
);
GO

-- Index pour recherche par type et entité
CREATE NONCLUSTERED INDEX IX_SearchIndexLog_EntityType_EntityId
ON dbo.SearchIndexLog(EntityType, EntityId);
GO

-- =============================================
-- 17. Table AppSetting
-- =============================================
IF OBJECT_ID('dbo.AppSetting', 'U') IS NOT NULL
DROP TABLE dbo.AppSetting;
GO

CREATE TABLE dbo.AppSetting (
    [Key] NVARCHAR(100) PRIMARY KEY,
    [Value] NVARCHAR(500) NOT NULL,
    Description NVARCHAR(255) NULL,
    UpdatedAt DATETIME NOT NULL DEFAULT GETDATE()
    );
GO

-- =============================================
-- Insertion des données de base
-- =============================================

-- Insertion des rôles par défaut
INSERT INTO dbo.Role (Name, Description) VALUES
('ADMIN', 'Administrateur système avec tous les droits'),
('LIBRARIAN', 'Bibliothécaire avec droits de gestion'),
('MEMBER', 'Membre standard de la bibliothèque');
GO

-- Insertion des paramètres applicatifs par défaut
INSERT INTO dbo.AppSetting ([Key], [Value], Description) VALUES
('DEFAULT_LOAN_DURATION_DAYS', '14', 'Durée d''emprunt par défaut en jours'),
('MAX_RENEWALS', '2', 'Nombre maximum de renouvellements par emprunt'),
('MAX_CONCURRENT_LOANS', '5', 'Nombre maximum d''emprunts simultanés par utilisateur'),
('MAX_RESERVATIONS', '3', 'Nombre maximum de réservations actives par utilisateur'),
('LATE_FEE_PER_DAY', '0.50', 'Amende par jour de retard (en devise locale)'),
('RESERVATION_EXPIRY_HOURS', '48', 'Délai en heures pour retirer une réservation prête'),
('REMINDER_DAYS_BEFORE_DUE', '3', 'Nombre de jours avant échéance pour envoyer un rappel');
GO

-- Insertion de catégories de base
INSERT INTO dbo.Category (Name, Slug, Description) VALUES
('Fiction', 'fiction', 'Œuvres de fiction'),
('Non-Fiction', 'non-fiction', 'Ouvrages documentaires'),
('Science-Fiction', 'science-fiction', 'Science-fiction et fantasy'),
('Roman', 'roman', 'Romans littéraires'),
('Histoire', 'histoire', 'Livres d''histoire'),
('Sciences', 'sciences', 'Ouvrages scientifiques'),
('Jeunesse', 'jeunesse', 'Livres pour enfants et adolescents'),
('Bande Dessinée', 'bande-dessinee', 'BD et mangas');
GO

PRINT 'Base de données créée avec succès!';
PRINT 'Tables créées: 17';
PRINT 'Rôles insérés: 3';
PRINT 'Paramètres insérés: 7';
PRINT 'Catégories insérées: 8';
GO


select * from Book;

UPDATE Book
SET CoverImageUrl = '/uploads/covers/itu.png';
