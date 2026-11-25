-- A_1_Beep_Global_Currency: time of each bank rate in dbo.Currency_Rate_Bank.
--
-- Each row is one bank's rate for one currency at one time: Date is the day the rate applies to and
-- RateTime is when the bank published it, in the bank's local time with its offset
-- (e.g. 2026-11-25 23:00:00 +07:00). The latest rate of a day is the one with the highest RateTime.
-- A rate whose RateTime is already recorded for that bank, currency and day is not inserted again.
--
-- Run once per environment before Beep.Api writes rates. Safe to run more than once.

IF COL_LENGTH('dbo.Currency_Rate_Bank', 'RateTime') IS NULL
	ALTER TABLE dbo.Currency_Rate_Bank ADD RateTime datetimeoffset NULL;
GO

-- Finds a day's rates and its latest one (and locks that range while a new rate is recorded).
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID('dbo.Currency_Rate_Bank') AND name = 'IX_currb_Bank_Currency_Date')
	CREATE NONCLUSTERED INDEX IX_currb_Bank_Currency_Date
		ON dbo.Currency_Rate_Bank (Bank_Id, Currency_Id, [Date], RateTime);
GO
