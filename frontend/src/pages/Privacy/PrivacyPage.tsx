import { Link } from 'react-router-dom';

export function PrivacyPage() {
  return (
    <div className="mx-auto max-w-3xl px-6 py-12">
      <h1 className="text-3xl font-bold text-gray-900">Privacy Notice</h1>
      <p className="mt-2 text-sm text-gray-500">Datenschutzerklaerung gemaess DSGVO</p>

      <div className="mt-8 space-y-8 text-sm text-gray-700 leading-relaxed">
        <section>
          <h2 className="text-lg font-semibold text-gray-900">1. Data Controller</h2>
          <p className="mt-2">
            Schuler Group, responsible for the SchulerPark parking booking system.
            For data protection inquiries, contact: <strong>datenschutz@schuler.de</strong>
          </p>
        </section>

        <section>
          <h2 className="text-lg font-semibold text-gray-900">2. What Data We Collect</h2>
          <ul className="mt-2 list-disc space-y-1 pl-5">
            <li><strong>Account data:</strong> Email address, display name, car license plate (optional)</li>
            <li><strong>Authentication data:</strong> Password hash (local accounts), Azure AD object ID (SSO accounts)</li>
            <li><strong>Booking data:</strong> Parking slot bookings including date, time slot, location, and status</li>
            <li><strong>Lottery data:</strong> Lottery participation history (win/loss records per location)</li>
            <li><strong>Technical data:</strong> IP address for refresh token security (stored temporarily)</li>
          </ul>
        </section>

        <section>
          <h2 className="text-lg font-semibold text-gray-900">3. Why We Collect It</h2>
          <ul className="mt-2 list-disc space-y-1 pl-5">
            <li><strong>Account & authentication:</strong> To provide access to the booking system (Art. 6(1)(b) DSGVO — contract performance)</li>
            <li><strong>Booking data:</strong> To manage parking slot assignments and confirmations</li>
            <li><strong>Lottery history:</strong> To ensure fair distribution of parking slots using weighted algorithms</li>
            <li><strong>Technical data:</strong> To prevent unauthorized access and detect token theft (Art. 6(1)(f) — legitimate interest)</li>
          </ul>
        </section>

        <section>
          <h2 className="text-lg font-semibold text-gray-900">4. Data Retention</h2>
          <p className="mt-2">
            Booking and lottery history data is automatically deleted after <strong>1 year</strong>.
            Aggregate statistics (total bookings per lottery run) are retained without personal data.
            A weekly automated job enforces this retention policy.
          </p>
        </section>

        <section>
          <h2 className="text-lg font-semibold text-gray-900">5. Your Rights</h2>
          <ul className="mt-2 list-disc space-y-1 pl-5">
            <li><strong>Right of access (Art. 15):</strong> Download all your personal data as JSON from your <Link to="/profile" className="text-blue-600 hover:underline">profile page</Link></li>
            <li><strong>Right to rectification (Art. 16):</strong> Update your display name and license plate in your profile</li>
            <li><strong>Right to erasure (Art. 17):</strong> Request account deletion from your profile page. Your account is deactivated immediately and permanently deleted after 30 days</li>
            <li><strong>Right to data portability (Art. 20):</strong> Export your data in machine-readable JSON format</li>
          </ul>
        </section>

        <section>
          <h2 className="text-lg font-semibold text-gray-900">6. Data Sharing</h2>
          <p className="mt-2">
            Your personal data is not shared with third parties. If you use Azure AD SSO,
            authentication is handled via Microsoft's identity platform. No booking data is transmitted externally.
          </p>
        </section>

        <section>
          <h2 className="text-lg font-semibold text-gray-900">7. Contact</h2>
          <p className="mt-2">
            For questions about data protection or to exercise your rights, contact:<br />
            <strong>Data Protection Officer</strong><br />
            Email: datenschutz@schuler.de
          </p>
        </section>
      </div>
    </div>
  );
}
