# Beep.Notification

Email alerts for every Beep website: subscribing, confirming (double opt-in), unsubscribing and sending.
Beep.Api hosts it and exposes it at `/api/notification/*`; each website adds its own kinds of alert.

## Data (A_1_Beep_Global_Default)

- **Subscribers are `dbo.User` rows.** Subscribing with a new email address creates a user:
  - `Login`: a random 9-digit number (100000000–999999999), drawn again if already taken.
  - `Password`: a bcrypt hash of a random password nobody knows. Subscribers don't log in.
  - `FullName` and `EmailAddress`: the email address.
  - `CultureInfoUI`: the website's language (en, vi, …).
  - `Licence_Id`: `Notification:LicenceId`.

  A user of that licence with the same email address is reused.
- **Subscriptions are `dbo.User_Notification` rows**, created by
  [`Resource/User_Notification.sql`](Resource/User_Notification.sql). There is one row per user, website and
  type, such as (`Currency`, `BankRates`):
  - `Filter`: the website's choices, as JSON.
  - `Token`: a secret used in the confirmation and unsubscribe links.
  - `IsConfirmed` and `ConfirmedAt`: the address was confirmed.
  - `IsDeleted`: unsubscribed.
  - `LastSentAt`: when the last email was sent.

  Subscribing again replaces the filter, makes the subscription active again and keeps it confirmed.

Apply the script to A_1_Beep_Global_Default before the first run. It is safe to run again.

## Flow

1. **Subscribe:** `SubscriptionService.SubscribeAsync`.
   - It checks the email address, the website (`Notification:Websites`) and the type (`INotificationType`).
     The type checks the filter.
   - It saves the subscription.
   - It queues a confirmation email, unless the address is already confirmed. That email isn't sent again
     within 5 minutes.
2. **Confirm:** the email's button opens the website's `/alerts/confirm?token=…`, which calls `ConfirmAsync`.
3. **Alerts:** the website's project sends them to `GetActiveAsync` (confirmed, not unsubscribed, active
   users). Every alert has the unsubscribe link `/alerts/unsubscribe?token=…`.

Emails are sent from a background queue (`INotificationQueue` and `NotificationWorker`), so no request
waits for SMTP. A failure is logged, and the next email is still sent.

## Adding an alert for a website

1. Add the website to `Notification:Websites:<Name>`:
   - `DisplayName`: used in emails.
   - `Url`: the website's address. The links in emails start with it.
2. Implement `INotificationType` (`Website`, `Name`, `NormalizeAsync` and `DescribeAsync`), and register it
   with `services.AddSingleton<INotificationType, …>()`.
3. Give the website `/alerts/confirm` and `/alerts/unsubscribe` pages that call the API.
4. When something happens, queue the sending: read `GetActiveAsync`, build the emails (`EmailLayout.Wrap`
   adds the frame and the unsubscribe link), send them with `INotificationEmailSender`, then call
   `MarkSentAsync`.

Beep.Api's `Notifications/BankRatesNotification.cs` is the example: Beep Currency's bank rate alerts.

Email texts are in the websites' thirteen languages (en, vi, th, zh, ja, ko, fr, de, es, id, ms, nl, ar) through
`EmailText`, in each subscriber's language. Arabic emails are laid out right to left.

## Configuration

| Key | Purpose |
| --- | --- |
| `ConnectionStrings:Default` | A_1_Beep_Global_Default |
| `Notification:LicenceId` | `dbo.Licence.Id` that new subscribers are created under |
| `Notification:Websites:<Name>:DisplayName` / `:Url` | Each website that takes subscriptions |
| `Notification:PickupDirectory` | Optional: without an SMTP host, emails are written here as .html files instead of being sent (local development) |
| `Mail:Smtp:From` | Sender address |
| `Mail:Smtp:Network:Host` / `Port` / `EnableSsl` / `Username` / `Password` / `DefaultCredentials` | SMTP server, used by Beep.Email.Core |

Without `Mail:Smtp:Network:Host`, nothing is sent. Emails go to `PickupDirectory` if it is set; otherwise a
warning is logged.
