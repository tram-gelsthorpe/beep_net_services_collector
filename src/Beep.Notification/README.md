# Beep.Notification

Email alerts for every Beep website: subscribing, confirming (double opt-in), unsubscribing and sending.
Beep.Platform.Api hosts it and exposes it at `/api/notification/*`; each website adds its own kinds of alert.

## Data (A_1_Beep_Global_Default)

- **Subscribers are `dbo.User` rows.** Subscribing with a new email address creates a user:
  - `Login`: a random 9-digit number (100000000–999999999), drawn again if already taken.
  - `Password`: a bcrypt hash of a random password nobody knows. Subscribers don't log in.
  - `FullName` and `EmailAddress`: the email address.
  - `CultureInfoUI`: the website's language (en, vi, …).
  - `Licence_Id`: `Notification:LicenceId`.

  A user of that licence with the same email address is reused.
- **Subscriptions are `dbo.User_Notification` rows**. Beep.Database.Processor creates the table
  (`Create/Global.sql`, and upgrade 6.4 for an older database). There is one row per user, website and
  type, such as (`Currency`, `BankRates`):
  - `Filter`: the website's choices, as JSON.
  - `Token`: a secret used in the confirmation and unsubscribe links.
  - `IsConfirmed` and `ConfirmedAt`: the address was confirmed.
  - `IsDeleted`: unsubscribed.
  - `LastSentAt`: when the last email was sent.

  Subscribing again replaces the filter, makes the subscription active again and keeps it confirmed.
- **Queued emails are `dbo.User_Notification_Email` rows** (`Create/Global.sql`, and upgrade 8.1), one per
  email:
  - `User_Notification_Id`: the subscription.
  - `Kind`: 1 = confirmation, 2 = alert.
  - `Data`: what the alert is about, as JSON, e.g. `{"bank":"BFTVVNVX"}`. Null for a confirmation.
  - `SentAt`: when it was sent. Null until then; a sent email is not sent again.

Apply the script to A_1_Beep_Global_Default before the first run. It is safe to run again.

## Flow

1. **Subscribe:** `SubscriptionService.SubscribeAsync`.
   - It checks the email address, the website (`Notification:Websites`) and the type (`INotificationType`).
     The type checks the filter.
   - It saves the subscription.
   - It queues a confirmation email, unless the address is already confirmed. That email isn't sent again
     within 5 minutes.
2. **Confirm:** the email's button opens the website's `/alerts/confirm?token=…`, which calls `ConfirmAsync`.
3. **Alerts:** the website's project queues them for `GetActiveAsync` (confirmed, not unsubscribed, active
   users). Every alert has the unsubscribe link `/alerts/unsubscribe?token=…`.

### Sending

Emails are queued, so no request waits for SMTP. `INotificationEmailQueue` adds one `User_Notification_Email`
row per subscription, and one ServiceBus work item for each row (worker type `NotificationEmail`, DomainId =
the row's `Id`). `NotificationEmailWorker` claims the work items, so any number of Beep.Platform.Api instances can
send without sending an email twice. For each one, `NotificationEmailWorkProcessor`:

1. Loads the row and the subscription as they are now. It skips the email (and finishes the work item) when
   it was already sent, the subscription was unsubscribed, a confirmation's address was confirmed meanwhile,
   or an alert has nothing to send.
2. Writes the email: `SubscriptionService.ConfirmationEmail`, or the type's `AlertEmailAsync`. An alert is
   written when it is sent, so a retry has the latest data.
3. Sends it, then records `SentAt` and the subscription's `LastSentAt`.

A failed send (SMTP down, mailbox full: a 4xx answer) is retried by the ServiceBus with exponential back-off,
up to `Notification:Email:MaxAttempts` attempts, then abandoned. An address the mail server refuses (a 5xx
answer) or a malformed address is not retried: the work item is finished with the reason.

## Adding an alert for a website

1. Add the website to `Notification:Websites:<Name>`:
   - `DisplayName`: used in emails.
   - `Url`: the website's address. The links in emails start with it.
2. Implement `INotificationType` (`Website`, `Name`, `NormalizeAsync`, `DescribeAsync` and `AlertEmailAsync`),
   and register it with `services.AddSingleton<INotificationType, …>()`. `AlertEmailAsync` writes one alert
   (`EmailLayout.Wrap` adds the frame and the unsubscribe link), or returns null when there is nothing to send.
3. Give the website `/alerts/confirm` and `/alerts/unsubscribe` pages that call the API.
4. When something happens, read `GetActiveAsync` and queue the alerts of the subscriptions it concerns with
   `INotificationEmailQueue.EnqueueAsync(ids, NotificationEmailKind.Alert, data)`. `data` is given back to
   `AlertEmailAsync`.

Beep.Platform.Api's `Websites/Currency/Notifications/BankRates/BankRatesNotification.cs` is the example: Beep Currency's bank rate alerts.

Email texts are in the websites' thirteen languages (en, vi, th, zh, ja, ko, fr, de, es, id, ms, nl, ar) through
`EmailText`, in each subscriber's language. Arabic emails are laid out right to left.

## Configuration

| Key | Purpose |
| --- | --- |
| `ConnectionStrings:Default` | A_1_Beep_Global_Default |
| `ConnectionStrings:ServiceBus` | Beep.Collector's ServiceBus database (the host registers `IWorkItemRepository` with it) |
| `Notification:LicenceId` | `dbo.Licence.Id` that new subscribers are created under |
| `Notification:Websites:<Name>:DisplayName` / `:Url` | Each website that takes subscriptions |
| `Notification:PickupDirectory` | Optional: without an SMTP host, emails are written here as .html files instead of being sent (local development) |
| `Notification:Email:MaxAttempts` | Optional: attempts of one email before it is abandoned (default 6) |
| `Notification:Email:MaxConcurrentItems` / `PollIntervalSeconds` / `LeaseSeconds` | Optional: the email worker (defaults 5, 5 and 120) |
| `Mail:Smtp:From` | Sender address |
| `Mail:Smtp:Network:Host` / `Port` / `EnableSsl` / `Username` / `Password` / `DefaultCredentials` | SMTP server, used by Beep.Email.Core |

Without `Mail:Smtp:Network:Host`, nothing is sent. Emails go to `PickupDirectory` if it is set; otherwise a
warning is logged.
