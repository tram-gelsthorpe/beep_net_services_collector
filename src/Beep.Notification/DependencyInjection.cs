using Beep.Email.Core;
using Beep.Infrastructure.Email;
using Beep.Notification.Email;
using Beep.Notification.Subscriptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Beep.Notification;

public static class DependencyInjection
{
	/// <summary>
	/// Subscriptions and emails for every website: <see cref="SubscriptionService"/>, the
	/// <see cref="INotificationQueue"/> and its worker, and sending with Beep.Email.Core (<c>Mail</c> section).
	/// Each website's project adds its <see cref="INotificationType"/>s.
	/// </summary>
	/// <param name="services">Services</param>
	/// <param name="configuration">With the <c>Notification</c> and <c>Mail</c> sections</param>
	/// <param name="connectionString">A_1_Beep_Global_Default</param>
	/// <param name="createdBy">User recorded as CreatedBy / ModifiedBy</param>
	public static IServiceCollection AddBeepNotification(
		this IServiceCollection services, IConfiguration configuration, string connectionString, Guid createdBy)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		var options = NotificationOptions.FromConfiguration(configuration, createdBy);
		services.AddSingleton(options);

		// Beep.Email.Core's Sender reads the Mail section (Mail:Smtp).
		services.AddEmailSetting(configuration);
		services.AddSingleton<ISender, Sender>();
		services.AddSingleton<INotificationEmailSender, NotificationEmailSender>();

		services.AddSingleton<ISubscriptionRepository>(new SubscriptionRepository(connectionString, options.LicenceId, createdBy));
		services.AddSingleton<SubscriptionService>();

		services.AddSingleton<NotificationQueue>();
		services.AddSingleton<INotificationQueue>(sp => sp.GetRequiredService<NotificationQueue>());
		services.AddHostedService<NotificationWorker>();
		return services;
	}
}
