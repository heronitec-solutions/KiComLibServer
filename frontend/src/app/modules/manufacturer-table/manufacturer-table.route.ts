import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ManufacturerTableModule } from './manufacturer-table.module';

@Component({
    selector: 'manufacturer-table-route',
    imports: [ManufacturerTableModule],
    template: '<manufacturer-table></manufacturer-table>',
    styles: [':host { display: contents; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class ManufacturerTableRouteComponent {}
