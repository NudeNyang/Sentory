export const MESSENGER_SOURCES = [
  "Discord",
  "KakaoTalk",
  "Slack",
  "WhatsApp",
  "Telegram",
  "Line",
  "WeChat",
];

export const MESSENGER_SOURCE_PATCH_KEYS = {
  Discord: "discordSupportEnabled",
  KakaoTalk: "kakaoTalkSupportEnabled",
  Slack: "slackSupportEnabled",
  WhatsApp: "whatsAppSupportEnabled",
  Telegram: "telegramSupportEnabled",
  Line: "lineSupportEnabled",
  WeChat: "weChatSupportEnabled",
};

export function createMessengerSourcePatch(
  enabledSources,
  discordAutoRestartConsentGranted = false,
) {
  const patch = {
    messengerDetectionSetupCompleted: true,
    discordAutoRestartConsentGranted:
      enabledSources.has("Discord") && discordAutoRestartConsentGranted,
  };
  for (const source of MESSENGER_SOURCES) {
    patch[MESSENGER_SOURCE_PATCH_KEYS[source]] = enabledSources.has(source);
  }
  return patch;
}

export function hasEnabledMessengerSource(settings) {
  return MESSENGER_SOURCES.some(source => Boolean(settings?.sources?.[source]));
}

export function needsMessengerSetup(settings) {
  return Boolean(settings) && !settings.messengerDetectionSetupCompleted;
}
