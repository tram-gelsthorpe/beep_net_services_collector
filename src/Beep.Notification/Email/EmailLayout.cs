using System.Net;

namespace Beep.Notification.Email;

/// <summary>
/// The frame of every notification email: the website's name, the content, and a footer with the
/// unsubscribe link. Inline styles only, so email clients keep them.
/// </summary>
public static class EmailLayout
{
	private static readonly EmailText Text = new(
	[
		["You get this email because you subscribed to {0} alerts.",
			"Bạn nhận email này vì đã đăng ký nhận thông báo của {0}.",
			"คุณได้รับอีเมลนี้เพราะสมัครรับการแจ้งเตือนของ {0}",
			"您收到此邮件，是因为您订阅了 {0} 的提醒。",
			"{0} のアラートに登録しているため、このメールが届いています。",
			"{0} 알림을 구독하셨기 때문에 이 이메일을 받으셨습니다.",
			"Vous recevez cet e-mail car vous êtes abonné aux alertes {0}.",
			"Sie erhalten diese E-Mail, weil Sie {0}-Alarme abonniert haben.",
			"Recibe este correo porque se suscribió a las alertas de {0}.",
			"Anda menerima email ini karena berlangganan peringatan {0}.",
			"Anda menerima e-mel ini kerana melanggan makluman {0}.",
			"U ontvangt deze e-mail omdat u zich hebt geabonneerd op meldingen van {0}."],
		["Unsubscribe", "Hủy đăng ký", "ยกเลิกการสมัคร", "退订", "配信停止", "구독 취소", "Se désabonner", "Abbestellen", "Cancelar suscripción", "Berhenti berlangganan", "Berhenti melanggan", "Afmelden"],
	]);

	public static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

	/// <summary>A whole email around <paramref name="contentHtml"/>.</summary>
	/// <param name="language">en, vi, …</param>
	/// <param name="websiteName">e.g. Beep Currency</param>
	/// <param name="contentHtml">Already HTML-encoded content</param>
	/// <param name="unsubscribeUrl">Link to the website's unsubscribe page; null for no footer link</param>
	public static string Wrap(string language, string websiteName, string contentHtml, string? unsubscribeUrl)
	{
		var footer = Encode(Text.Format("You get this email because you subscribed to {0} alerts.", language, websiteName));
		if (unsubscribeUrl is not null)
			footer += $" <a href=\"{Encode(unsubscribeUrl)}\" style=\"color:#475467\">{Encode(Text.Get("Unsubscribe", language))}</a>";

		return $"""
			<!DOCTYPE html>
			<html lang="{Encode(language)}">
			<head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"></head>
			<body style="margin:0;padding:24px 12px;background:#EEF2FF;font-family:Segoe UI,Arial,sans-serif;color:#101828">
			<table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center">
			<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:600px">
			<tr><td style="padding:0 4px 16px;font-size:18px;font-weight:700;color:#1E2A78">{Encode(websiteName)}</td></tr>
			<tr><td style="background:#ffffff;border-radius:16px;padding:28px">{contentHtml}</td></tr>
			<tr><td style="padding:16px 4px;font-size:12px;line-height:1.5;color:#667085">{footer}</td></tr>
			</table>
			</td></tr></table>
			</body>
			</html>
			""";
	}

	/// <summary>A big button link.</summary>
	public static string Button(string url, string text) =>
		$"<a href=\"{Encode(url)}\" style=\"display:inline-block;padding:14px 24px;border-radius:12px;background:#F97316;color:#ffffff;font-weight:600;text-decoration:none\">{Encode(text)}</a>";
}
