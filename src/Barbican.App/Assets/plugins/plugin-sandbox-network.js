// Trusted child bootstrap. The policy check runs before importing the verified SDK.
async function confirmsWebRtcBlock() {
  if (typeof ReportingObserver !== 'function' || typeof RTCPeerConnection !== 'function') return false;

  let observer;
  let timer;
  try {
    let finish;
    const reported = new Promise(resolve => { finish = resolve; });
    const inspect = reports => {
      if (reports.some(report => report.type === 'connection-allowlist' &&
          report.body?.connection === 'webrtc' && report.body?.disposition === 'enforce')) finish(true);
    };
    observer = new ReportingObserver(inspect, { types: ['connection-allowlist'] });
    observer.observe();
    // No ICE servers, descriptions, channels, offers or negotiation.
    try { new RTCPeerConnection({ iceServers: [] }).close(); } catch { }
    inspect(observer.takeRecords());
    timer = setTimeout(() => finish(false), 2000);
    return await reported;
  } catch {
    return false;
  } finally {
    clearTimeout(timer);
    observer?.disconnect();
  }
}

const entry = document.querySelector('meta[name="barbican-runtime"]')?.content;
if (entry && await confirmsWebRtcBlock()) await import(entry);
