import { DistributorProduct } from './distributor-search.service';
import { importFieldOptions, toggleImportField } from './import-fields';

function product(partial: Partial<DistributorProduct> & Pick<DistributorProduct, 'provider'>): DistributorProduct {
  return {
    manufacturer: null,
    manufacturerPartNumber: null,
    description: null,
    datasheetUrl: null,
    productUrl: null,
    imageUrl: null,
    lifecycleStatus: null,
    package: null,
    distributorName: null,
    orderNumber: null,
    quantityMin: null,
    quantitySteps: null,
    ...partial
  };
}

function option(providerProduct: DistributorProduct, id: string) {
  const found = importFieldOptions(providerProduct).find(item => item.id === id);
  if (!found) {
    throw new Error(`Missing field ${id}`);
  }

  return found;
}

describe('importFieldOptions', () => {
  const full = product({
    provider: 'digikey',
    manufacturer: 'Texas Instruments',
    manufacturerPartNumber: 'LM358DR',
    description: 'Dual operational amplifier',
    datasheetUrl: 'https://example.com/lm358.pdf',
    imageUrl: 'https://media.digikey.com/photos/lm358.jpg',
    lifecycleStatus: 'active',
    package: 'SOIC-8',
    distributorName: 'DigiKey',
    orderNumber: '296-1395-1-ND',
    quantityMin: 1,
    quantitySteps: 1
  });

  it('offers every field a complete DigiKey hit contains', () => {
    const options = importFieldOptions(full);

    expect(options.map(item => item.id)).toEqual([
      'description', 'status', 'image', 'datasheet', 'partNumber', 'package', 'order'
    ]);
    expect(options.every(item => item.available)).toBeTrue();
    expect(option(full, 'partNumber').detail).toBe('Texas Instruments · LM358DR');
    expect(option(full, 'order').detail).toBe('DigiKey · 296-1395-1-ND · min 1, step 1');
    expect(option(full, 'status').detail).toBe('Active');
  });

  it('greys out fields this DigiKey part does not include', () => {
    const hit = product({
      ...full,
      description: null,
      imageUrl: '  ',
      datasheetUrl: null
    });

    expect(option(hit, 'description')).toEqual(jasmine.objectContaining({
      available: false,
      reason: 'Not available for this part'
    }));
    expect(option(hit, 'image').reason).toBe('Not available for this part');
    expect(option(hit, 'datasheet').reason).toBe('Not available for this part');
    expect(option(hit, 'partNumber').available).toBeTrue();
  });

  it('greys out fields the API never returns', () => {
    const hit = product({
      provider: 'tme',
      manufacturer: 'ST',
      manufacturerPartNumber: 'LM358DR',
      description: 'Op amp',
      datasheetUrl: 'https://www.tme.eu/Document/lm358.pdf',
      distributorName: 'TME',
      orderNumber: 'LM358DR-TME',
      quantityMin: 1,
      quantitySteps: 5
    });

    expect(option(hit, 'status').reason).toBe('TME does not provide this');
    expect(option(hit, 'image').reason).toBe('TME does not provide this');
    expect(option(hit, 'package').reason).toBe('TME does not provide this');
    expect(option(hit, 'description').available).toBeTrue();
    expect(option(hit, 'datasheet').detail).toBe('lm358.pdf');
    expect(option(hit, 'order').available).toBeTrue();
  });

  it('keeps a package selectable when this Mouser hit actually has one', () => {
    const without = product({
      provider: 'mouser',
      manufacturerPartNumber: 'LM358DR',
      orderNumber: '595-LM358DR'
    });
    const withPackage = product({
      provider: 'mouser',
      manufacturerPartNumber: 'LM358DR',
      package: 'Tape & Reel'
    });

    expect(option(without, 'package').reason).toBe('Mouser does not provide this');
    expect(option(without, 'image').reason).toBe('Mouser does not provide this');
    expect(option(without, 'datasheet').reason).toBe('Mouser does not provide this');
    expect(option(withPackage, 'package')).toEqual(jasmine.objectContaining({
      available: true,
      detail: 'Tape & Reel',
      reason: ''
    }));
    expect(option(product({ provider: 'farnell', manufacturerPartNumber: 'LM358DR' }), 'image').reason)
      .toBe('Farnell does not provide this');
    expect(option(product({ provider: 'arrow', manufacturerPartNumber: 'LM358DR' }), 'package').reason)
      .toBe('Not available for this part');
  });

  it('does not offer an order number that has no manufacturer part number to attach to', () => {
    const hit = product({
      provider: 'digikey',
      orderNumber: '296-1395-1-ND',
      package: 'SOIC-8'
    });

    expect(option(hit, 'order').available).toBeFalse();
    expect(option(hit, 'package').available).toBeFalse();
    expect(option(hit, 'partNumber').reason).toBe('Not available for this part');
  });
});

describe('toggleImportField', () => {
  it('selects the part number when the order or package is selected', () => {
    const order = toggleImportField(new Set(['description']), 'order', true);
    expect(order.has('order')).toBeTrue();
    expect(order.has('partNumber')).toBeTrue();

    const pack = toggleImportField(new Set(), 'package', true);
    expect(pack.has('package')).toBeTrue();
    expect(pack.has('partNumber')).toBeTrue();
  });

  it('clears the order and package when the part number is cleared', () => {
    const next = toggleImportField(new Set(['partNumber', 'order', 'package', 'status']), 'partNumber', false);

    expect(Array.from(next)).toEqual(['status']);
  });
});
