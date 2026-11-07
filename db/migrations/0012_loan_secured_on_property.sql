-- Whether a loan is secured on the home or another illiquid asset (#135), so liquid net worth can
-- leave it out together with the property. Guarded so it is safe if the script runs again.

IF COL_LENGTH(N'app.Loan', N'SecuredOnProperty') IS NULL
    ALTER TABLE app.Loan ADD SecuredOnProperty bit NOT NULL
        CONSTRAINT DF_Loan_SecuredOnProperty DEFAULT 0;
