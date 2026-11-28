using Beep.Email.Core;
using Beep.Infrastructure.Email;
using Beep.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Beep.Notification;

public static class DependencyInjection
{
	/// <summary>
	/// Subscriptions and emails for every website: <see cref="SubscriptionService"/>, the
	/// <see cref="INotificationEmailQueue"/> and its worker, and sending with Beep.Email.Core (<c>Mail</c> section).
	/// Each website's project adds its <see cref="INotificationType"/>s.
	/// The host must also register <see cref="IWorkItemRepository"/>: emails are queued, claimed and retried
	/// through the ServiceBus (<c>Notification:Email</c> section).
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

		var emailOptions = new NotificationEmailOptions();
		configuration.GetSection(NotificationEmailOptions.Section).Bind(emailOptions);
		emailOptions.Validate();
		services.AddSingleton(emailOptions);
		services.AddSingleton<INotificationEmailRepository>(new NotificationEmailRepository(connectionString, createdBy));
		services.AddSingleton<INotificationEmailQueue, NotificationEmailQueue>();
		services.AddSingleton<NotificationEmailWorkProcessor>();
		services.AddHostedService<NotificationEmailWorker>();
		return services;
	}
}
