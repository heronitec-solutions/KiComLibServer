export type ApiProviderId = 'trustedparts' | 'mouser' | 'digikey' | 'farnell' | 'arrow' | 'tme';

export interface ApiProviderDefinition {
  id: ApiProviderId;
  label: string;
  kind: 'catalog' | 'distributor';
  kindLabel: string;
  /** Text before the documentation link. */
  lead: string;
  /** Text after the documentation link. */
  tail: string;
  docsUrl: string;
  docsLabel: string;
}

export const API_PROVIDERS: readonly ApiProviderDefinition[] = [
  {
    id: 'trustedparts',
    label: 'TrustedParts',
    kind: 'catalog',
    kindLabel: 'Catalog',
    lead: 'Company ID and API key for the',
    tail: 'The key is stored encrypted and is not shown again. Leave it empty to keep the saved key.',
    docsUrl: 'https://www.trustedparts.com/en/docs/api/trustedparts-api/',
    docsLabel: 'TrustedParts Inventory API'
  },
  {
    id: 'mouser',
    label: 'Mouser',
    kind: 'distributor',
    kindLabel: 'Distributor',
    lead: 'API key for the',
    tail: 'The key is stored encrypted and is not shown again. Leave it empty to keep the saved key.',
    docsUrl: 'https://api.mouser.com/',
    docsLabel: 'Mouser API'
  },
  {
    id: 'digikey',
    label: 'DigiKey',
    kind: 'distributor',
    kindLabel: 'Distributor',
    lead: 'Client ID, client secret, and locale for the',
    tail: 'The secret is stored encrypted and is not shown again. Leave it empty to keep the saved secret.',
    docsUrl: 'https://developer.digikey.com/',
    docsLabel: 'DigiKey API'
  },
  {
    id: 'farnell',
    label: 'Farnell',
    kind: 'distributor',
    kindLabel: 'Distributor',
    lead: 'API key and store for the',
    tail: 'The key is stored encrypted and is not shown again. Leave it empty to keep the saved key.',
    docsUrl: 'https://partner.element14.com/',
    docsLabel: 'element14 Partner API'
  },
  {
    id: 'arrow',
    label: 'Arrow',
    kind: 'distributor',
    kindLabel: 'Distributor',
    lead: 'Login and API key for the',
    tail: 'The key is stored encrypted and is not shown again. Leave it empty to keep the saved key.',
    docsUrl: 'https://www.arrow.com/',
    docsLabel: 'Arrow API'
  },
  {
    id: 'tme',
    label: 'TME',
    kind: 'distributor',
    kindLabel: 'Distributor',
    lead: 'Token, app secret, country, and language for the',
    tail: 'Secrets are stored encrypted and are not shown again. Leave a secret empty to keep the saved value.',
    docsUrl: 'https://developers.tme.eu/en/api-doc/v2',
    docsLabel: 'TME API'
  }
];

export function apiProvider(id: string): ApiProviderDefinition | undefined {
  return API_PROVIDERS.find(provider => provider.id === id);
}
