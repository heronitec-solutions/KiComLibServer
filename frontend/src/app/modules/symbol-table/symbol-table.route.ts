import { ChangeDetectionStrategy, Component } from '@angular/core';
import { SymbolTableModule } from './symbol-table.module';

@Component({
    selector: 'symbol-table-route',
    imports: [SymbolTableModule],
    template: '<symbol-table></symbol-table>',
    styles: [':host { display: contents; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class SymbolTableRouteComponent {}
