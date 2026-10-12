import { ChangeDetectionStrategy, Component } from '@angular/core';
import { CadModelTableModule } from './cad-model-table.module';

@Component({
    selector: 'cad-model-table-route',
    imports: [CadModelTableModule],
    template: '<cad-model-table></cad-model-table>',
    styles: [':host { display: contents; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class CadModelTableRouteComponent {}
